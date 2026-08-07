using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Placeholders;
using Flirty.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Placeholders;

/// <summary>
/// End-to-end verification of message placeholders (issue #140) over the full DI stack and the facade
/// <see cref="IFlirtyEngine"/>: a marker in a question text and in an answer-option label is filled at
/// delivery time by a host filler, the filler shares the handler's scoped <see cref="FlirtyDbContext"/>,
/// a dialog without placeholders is delivered unchanged, the resolved value is never persisted, and a
/// broken placeholder degrades without breaking the run.
/// </summary>
public sealed class PlaceholderRuntimeTests
{
    /// <summary>Scoped probe that captures the <see cref="FlirtyDbContext"/> a filler was handed.</summary>
    private sealed class ContextProbe
    {
        public FlirtyDbContext? SeenContext { get; set; }
    }

    /// <summary>A filler that greets by the session's user key and records its injected scoped context.</summary>
    private sealed class UserNameFiller : IPlaceholderFiller
    {
        private readonly FlirtyDbContext _db;
        private readonly ContextProbe _probe;

        public UserNameFiller(FlirtyDbContext db, ContextProbe probe)
        {
            _db = db;
            _probe = probe;
        }

        public ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken)
        {
            _probe.SeenContext = _db;
            return new ValueTask<string?>(context.ExternalUserKey == "alice" ? "Alice" : context.ExternalUserKey);
        }
    }

    /// <summary>A filler that always throws – to prove the run survives a misbehaving one.</summary>
    private sealed class ThrowingFiller : IPlaceholderFiller
    {
        public ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    private static (ServiceProvider Provider, SqliteConnection KeepAlive) BuildProvider(
        Action<FlirtyOptions> configure)
    {
        var connectionString = $"Data Source=FlirtyPlaceholderTest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var provider = new ServiceCollection()
            .AddLogging()
            .AddScoped<ContextProbe>()
            .AddFlirty(options =>
            {
                options.UseSqlite(connectionString);
                configure(options);
            })
            .BuildServiceProvider();

        return (provider, keepAlive);
    }

    private static void SeedGreeting(ServiceProvider provider, string text, string? optionLabel = null)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
        context.Database.EnsureCreated();

        var dialogId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var question = new Question
        {
            Id = questionId,
            DialogId = dialogId,
            Key = "greeting",
            Text = text,
            Type = optionLabel is null ? QuestionType.FreeText : QuestionType.SingleChoice,
            Order = 0,
        };

        if (optionLabel is not null)
        {
            question.Options.Add(new AnswerOption
            {
                Id = Guid.NewGuid(), QuestionId = questionId, Key = "a", Label = optionLabel, Value = "a", Order = 0,
            });
        }

        context.Dialogs.Add(new Dialog
        {
            Id = dialogId,
            Key = "greet",
            Name = "Greeting",
            Version = 1,
            IsPublished = true,
            StartQuestionId = questionId,
            CreatedAt = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
            Questions = { question },
        });
        context.SaveChanges();
    }

    [Fact]
    public async Task A_placeholder_in_the_text_is_filled_and_the_filler_shares_the_handlers_db_context()
    {
        var (provider, keepAlive) = BuildProvider(
            options => options.AddPlaceholder<UserNameFiller>("user-name", "User name"));

        using (keepAlive)
        using (provider)
        {
            SeedGreeting(provider, "Hello {{user-name}}");

            using var scope = provider.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

            var start = await engine.StartDialogAsync("greet", "alice");

            Assert.Equal("Hello Alice", start.CurrentQuestion.Text);

            // The filler resolved the SAME scoped FlirtyDbContext the handler uses (proof of the shared
            // request scope, not a second one).
            Assert.Same(
                scope.ServiceProvider.GetRequiredService<FlirtyDbContext>(),
                scope.ServiceProvider.GetRequiredService<ContextProbe>().SeenContext);
        }
    }

    [Fact]
    public async Task A_placeholder_in_an_option_label_is_filled()
    {
        var (provider, keepAlive) = BuildProvider(
            options => options.AddPlaceholder<UserNameFiller>("user-name", "User name"));

        using (keepAlive)
        using (provider)
        {
            SeedGreeting(provider, "Pick one", optionLabel: "You are {{user-name}}");

            using var scope = provider.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

            var start = await engine.StartDialogAsync("greet", "alice");

            Assert.Equal("You are Alice", Assert.Single(start.CurrentQuestion.Options).Label);
        }
    }

    [Fact]
    public async Task A_dialog_without_placeholders_is_delivered_unchanged()
    {
        // No AddPlaceholder at all: gated by absence, so even a marker in the text stays raw – byte for
        // byte what a dialog delivered before this feature.
        var (provider, keepAlive) = BuildProvider(_ => { });

        using (keepAlive)
        using (provider)
        {
            SeedGreeting(provider, "Hello {{user-name}}");

            using var scope = provider.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

            var start = await engine.StartDialogAsync("greet", "alice");

            Assert.Equal("Hello {{user-name}}", start.CurrentQuestion.Text);
        }
    }

    [Fact]
    public async Task The_resolved_value_is_never_persisted()
    {
        var (provider, keepAlive) = BuildProvider(
            options => options.AddPlaceholder<UserNameFiller>("user-name", "User name"));

        using (keepAlive)
        using (provider)
        {
            SeedGreeting(provider, "Hello {{user-name}}");

            using (var scope = provider.CreateScope())
            {
                var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();
                var start = await engine.StartDialogAsync("greet", "alice");
                Assert.Equal("Hello Alice", start.CurrentQuestion.Text);
            }

            // The stored configuration keeps the raw marker: the fill is a delivery-time concern only.
            using var assertScope = provider.CreateScope();
            var context = assertScope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            var dialog = context.Dialogs.Include(entity => entity.Questions).Single();
            var question = Assert.Single(dialog.Questions);
            Assert.Equal("Hello {{user-name}}", question.Text);
        }
    }

    [Fact]
    public async Task An_unknown_marker_degrades_without_breaking_the_start()
    {
        // A placeholder is declared (so the renderer scans), but the message references a different key.
        var (provider, keepAlive) = BuildProvider(
            options => options.AddPlaceholder<UserNameFiller>("user-name", "User name"));

        using (keepAlive)
        using (provider)
        {
            SeedGreeting(provider, "Order {{order-id}}");

            using var scope = provider.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

            var start = await engine.StartDialogAsync("greet", "alice");

            Assert.Equal("Order {{order-id}}", start.CurrentQuestion.Text);
        }
    }

    [Fact]
    public async Task A_throwing_filler_degrades_without_breaking_the_start()
    {
        var (provider, keepAlive) = BuildProvider(
            options => options.AddPlaceholder<ThrowingFiller>("user-name", "User name"));

        using (keepAlive)
        using (provider)
        {
            SeedGreeting(provider, "Hello {{user-name}}");

            using var scope = provider.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

            var start = await engine.StartDialogAsync("greet", "alice");

            Assert.Equal("Hello {{user-name}}", start.CurrentQuestion.Text);
        }
    }
}
