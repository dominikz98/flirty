using Flirty.Mcp;
using Flirty.Mcp.Tools;
using Flirty.Placeholders;

namespace Flirty.Tests.Mcp;

/// <summary>
/// Verifies <c>flirty_placeholder_list</c> (#140): what an MCP client learns about the message
/// placeholders a host declared, and – the point of the tool – that it learns nothing else. The twin of
/// <see cref="FlirtyQuestionTypeToolsTests"/>.
/// </summary>
public sealed class FlirtyPlaceholderToolsTests
{
    /// <summary>Test double: exists only so a declaration has a filler type to carry.</summary>
    private sealed class UserNameFiller : IPlaceholderFiller
    {
        public ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken)
            => new("Alice");
    }

    private static Task<FlirtyMcpTestHost> StartWithPlaceholdersAsync()
        => FlirtyMcpTestHost.StartAsync(configureFlirty: options => options
            .AddPlaceholder("today", "Today's date", "2026-08-07")
            .AddPlaceholder<UserNameFiller>("user-name", "User name", "Alice"));

    [Fact]
    public async Task ListPlaceholders_reports_the_declared_placeholders_ordered_by_key()
    {
        await using var host = await StartWithPlaceholdersAsync();

        var result = await host.CallAsync<FlirtyPlaceholderList>(FlirtyToolNames.PlaceholderList);

        Assert.Null(result.Note);
        Assert.Collection(
            result.Placeholders,
            placeholder =>
            {
                Assert.Equal("today", placeholder.Key);
                Assert.Equal("Today's date", placeholder.DisplayName);
                Assert.Equal("2026-08-07", placeholder.Sample);
            },
            placeholder =>
            {
                Assert.Equal("user-name", placeholder.Key);
                Assert.Equal("User name", placeholder.DisplayName);
                Assert.Equal("Alice", placeholder.Sample);
            });
    }

    /// <summary>
    /// The registered filler is a server-side CLR type and has no business on the wire. Asserted on the
    /// <b>serialized text</b> rather than the projection's members, because restating the record's
    /// declaration would prove nothing: <c>System.Text.Json</c> ignores accessibility, so <c>internal</c>
    /// is no protection – every result wrapper in this package reaches the client in full.
    /// </summary>
    [Fact]
    public async Task ListPlaceholders_does_not_put_the_filler_type_on_the_wire()
    {
        await using var host = await StartWithPlaceholdersAsync();

        var result = await host.Mcp.CallToolAsync(FlirtyToolNames.PlaceholderList);

        var raw = result.StructuredContent!.Value.GetRawText();
        Assert.DoesNotContain("UserNameFiller", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fillerType", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user-name", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty case is the default host, and it must not read as a failure or a permission problem – the
    /// same reason <c>flirty_question_type_list</c> carries a note.
    /// </summary>
    [Fact]
    public async Task ListPlaceholders_explains_an_empty_list()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var result = await host.CallAsync<FlirtyPlaceholderList>(FlirtyToolNames.PlaceholderList);

        Assert.Empty(result.Placeholders);
        Assert.NotNull(result.Note);
    }
}
