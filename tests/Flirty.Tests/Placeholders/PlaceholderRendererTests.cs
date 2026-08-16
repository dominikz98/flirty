using Flirty.Domain;
using Flirty.Placeholders;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flirty.Tests.Placeholders;

/// <summary>
/// Verifies the <see cref="PlaceholderRenderer"/> (issue #140): filling markers in the question text and
/// option labels, the best-effort degradation (unknown key, filler-less declaration, a throwing or
/// null-returning filler), the gated-by-absence short-circuit, the per-key cache and the one-level
/// recursion rule.
/// </summary>
public sealed class PlaceholderRendererTests
{
    private const string Key = "user-name";

    /// <summary>Hand-written filler double: records calls and the last context, produces a configured value.</summary>
    private sealed class RecordingFiller : IPlaceholderFiller
    {
        private readonly Func<PlaceholderContext, string?> _produce;

        public RecordingFiller()
            : this(_ => "Alice")
        {
        }

        public RecordingFiller(Func<PlaceholderContext, string?> produce) => _produce = produce;

        public int Calls { get; private set; }

        public PlaceholderContext? LastContext { get; private set; }

        public ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken)
        {
            Calls++;
            LastContext = context;
            return new ValueTask<string?>(_produce(context));
        }
    }

    /// <summary>An <see cref="IServiceProvider"/> that records every resolution and resolves nothing.</summary>
    private sealed class RecordingServiceProvider : IServiceProvider
    {
        public int Calls { get; private set; }

        public object? GetService(Type serviceType)
        {
            Calls++;
            return null;
        }
    }

    private static FlirtyPlaceholderRegistry Registry(Type? fillerType, string key = Key)
        => new(new Dictionary<string, FlirtyPlaceholder>(StringComparer.Ordinal)
        {
            [key] = new(key, "User name", fillerType, "Alice"),
        });

    private static (PlaceholderRenderer Renderer, RecordingLoggerProvider Logs) Build(
        FlirtyPlaceholderRegistry registry, IPlaceholderFiller? filler = null)
    {
        var logs = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        if (filler is not null)
        {
            services.AddSingleton(filler.GetType(), filler);
        }

        var provider = services.BuildServiceProvider();

        return (
            new PlaceholderRenderer(
                registry, provider, provider.GetRequiredService<ILogger<PlaceholderRenderer>>()),
            logs);
    }

    private static Dialog DialogWith(string text, params (string Key, string Label)[] options)
    {
        var dialogId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        var question = new Question
        {
            Id = questionId,
            DialogId = dialogId,
            Key = "greeting",
            Text = text,
            Type = QuestionType.SingleChoice,
            Order = 0,
        };

        var order = 0;
        foreach (var (key, label) in options)
        {
            question.Options.Add(new AnswerOption
            {
                Id = Guid.NewGuid(), QuestionId = questionId, Key = key, Label = label, Value = key, Order = order++,
            });
        }

        return new Dialog
        {
            Id = dialogId,
            Key = "welcome",
            Name = "Welcome",
            Version = 1,
            IsPublished = true,
            StartQuestionId = questionId,
            CreatedAt = TestDialogFactory.SampleTime,
            UpdatedAt = TestDialogFactory.SampleTime,
            Questions = { question },
        };
    }

    private static DialogSession SessionFor(Dialog dialog) => new()
    {
        Id = Guid.NewGuid(),
        DialogId = dialog.Id,
        DialogVersion = dialog.Version,
        ExternalUserKey = "user-1",
        Status = SessionStatus.InProgress,
        CurrentQuestionId = dialog.StartQuestionId,
        StartedAt = TestDialogFactory.SampleTime,
    };

    private static ValueTask<QuestionView> RenderAsync(
        PlaceholderRenderer renderer, Dialog dialog)
        => renderer.RenderAsync(dialog, SessionFor(dialog), dialog.StartQuestionId, default);

    // ---- Filling ----------------------------------------------------------------------------

    [Fact]
    public async Task Fills_a_marker_in_the_question_text()
    {
        var filler = new RecordingFiller();
        var (renderer, _) = Build(Registry(typeof(RecordingFiller)), filler);

        var view = await RenderAsync(renderer, DialogWith("Hello {{user-name}}"));

        Assert.Equal("Hello Alice", view.Text);
        Assert.Equal(1, filler.Calls);
    }

    [Fact]
    public async Task Fills_a_marker_in_an_option_label()
    {
        var filler = new RecordingFiller();
        var (renderer, _) = Build(Registry(typeof(RecordingFiller)), filler);

        var view = await RenderAsync(renderer, DialogWith("Pick one", ("a", "Greet {{user-name}}")));

        Assert.Equal("Greet Alice", Assert.Single(view.Options).Label);
    }

    [Fact]
    public async Task Fills_the_same_marker_in_text_and_label_consistently()
    {
        var filler = new RecordingFiller();
        var (renderer, _) = Build(Registry(typeof(RecordingFiller)), filler);

        var view = await RenderAsync(renderer, DialogWith("Hello {{user-name}}", ("a", "You are {{user-name}}")));

        Assert.Equal("Hello Alice", view.Text);
        Assert.Equal("You are Alice", Assert.Single(view.Options).Label);

        // The value is cached per render: one filler call despite two occurrences.
        Assert.Equal(1, filler.Calls);
    }

    [Fact]
    public async Task The_context_carries_the_running_session_facts()
    {
        var filler = new RecordingFiller();
        var (renderer, _) = Build(Registry(typeof(RecordingFiller)), filler);
        var dialog = DialogWith("Hello {{user-name}}");
        var session = SessionFor(dialog);

        await renderer.RenderAsync(dialog, session, dialog.StartQuestionId, default);

        var context = filler.LastContext!;
        Assert.Equal(Key, context.Key);
        Assert.Equal(session.Id, context.SessionId);
        Assert.Equal("user-1", context.ExternalUserKey);
        Assert.Equal(dialog.Id, context.DialogId);
        Assert.Equal("welcome", context.DialogKey);
        Assert.Equal("greeting", context.QuestionKey);
        Assert.Same(session, context.ExpressionContext.Session);
    }

    // ---- Best-effort degradation ------------------------------------------------------------

    [Fact]
    public async Task An_unknown_key_is_left_raw_and_logs_a_warning()
    {
        // A placeholder is declared, so the renderer scans; the referenced key is a different one.
        var (renderer, logs) = Build(Registry(typeof(RecordingFiller)), new RecordingFiller());

        var view = await RenderAsync(renderer, DialogWith("Hello {{missing}}"));

        Assert.Equal("Hello {{missing}}", view.Text);
        var warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("missing", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_filler_less_placeholder_is_left_raw_and_logs_a_warning()
    {
        var (renderer, logs) = Build(Registry(fillerType: null));

        var view = await RenderAsync(renderer, DialogWith("Hello {{user-name}}"));

        Assert.Equal("Hello {{user-name}}", view.Text);
        Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_throwing_filler_degrades_to_raw_and_logs()
    {
        var filler = new RecordingFiller(_ => throw new InvalidOperationException("boom"));
        var (renderer, logs) = Build(Registry(typeof(RecordingFiller)), filler);

        var view = await RenderAsync(renderer, DialogWith("Hello {{user-name}}"));

        Assert.Equal("Hello {{user-name}}", view.Text);
        var warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.NotNull(warning.Exception);
    }

    [Fact]
    public async Task A_null_returning_filler_degrades_to_raw_and_logs()
    {
        var filler = new RecordingFiller(_ => null);
        var (renderer, logs) = Build(Registry(typeof(RecordingFiller)), filler);

        var view = await RenderAsync(renderer, DialogWith("Hello {{user-name}}"));

        Assert.Equal("Hello {{user-name}}", view.Text);
        Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task One_misbehaving_placeholder_does_not_poison_the_rest_of_the_text()
    {
        // 'user-name' resolves, 'missing' degrades - the good one is still filled.
        var filler = new RecordingFiller();
        var (renderer, _) = Build(Registry(typeof(RecordingFiller)), filler);

        var view = await RenderAsync(renderer, DialogWith("Hello {{user-name}}, ref {{missing}}"));

        Assert.Equal("Hello Alice, ref {{missing}}", view.Text);
    }

    // ---- Gated by absence -------------------------------------------------------------------

    [Fact]
    public async Task An_empty_registry_returns_the_projection_untouched_without_touching_the_scope()
    {
        var probe = new RecordingServiceProvider();
        var renderer = new PlaceholderRenderer(
            FlirtyPlaceholderRegistry.Empty, probe, NullLogger<PlaceholderRenderer>.Instance);
        var dialog = DialogWith("Hello {{user-name}}", ("a", "Pick {{user-name}}"));

        var view = await renderer.RenderAsync(dialog, SessionFor(dialog), dialog.StartQuestionId, default);

        Assert.Equal("Hello {{user-name}}", view.Text);
        Assert.Equal("Pick {{user-name}}", Assert.Single(view.Options).Label);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task A_text_without_a_marker_resolves_no_filler()
    {
        var filler = new RecordingFiller();
        var (renderer, _) = Build(Registry(typeof(RecordingFiller)), filler);

        var view = await RenderAsync(renderer, DialogWith("No markers here"));

        Assert.Equal("No markers here", view.Text);
        Assert.Equal(0, filler.Calls);
    }

    // ---- Syntax and recursion ---------------------------------------------------------------

    [Fact]
    public async Task A_token_outside_the_charset_is_not_a_marker_and_stays_verbatim()
    {
        var filler = new RecordingFiller();
        var (renderer, _) = Build(Registry(typeof(RecordingFiller)), filler);

        // Uppercase and underscore are outside [a-z0-9-]: no match, so no fill and no filler call.
        var view = await RenderAsync(renderer, DialogWith("Hi {{User_Name}} and {{ user-name }}"));

        Assert.Equal("Hi {{User_Name}} and {{ user-name }}", view.Text);
        Assert.Equal(0, filler.Calls);
    }

    [Fact]
    public async Task A_filled_value_is_not_re_scanned()
    {
        // The filler returns a value that itself looks like a marker; it must not be expanded again.
        var filler = new RecordingFiller(_ => "{{other}}");
        var (renderer, _) = Build(Registry(typeof(RecordingFiller)), filler);

        var view = await RenderAsync(renderer, DialogWith("Hello {{user-name}}"));

        Assert.Equal("Hello {{other}}", view.Text);
        Assert.Equal(1, filler.Calls);
    }
}
