using Flirty.Domain;
using Flirty.Mcp;
using Flirty.Mcp.Tools;
using Flirty.Runtime.Admin;
using Flirty.Validation;
using Microsoft.Extensions.DependencyInjection;
using static Flirty.Tests.Mcp.FlirtyMcpToolCalls;

namespace Flirty.Tests.Mcp;

/// <summary>
/// Verifies <c>flirty_question_type_list</c> (#136): what an MCP client learns about the custom question
/// types a host declared, and – the point of the tool – that it learns nothing else.
/// </summary>
public sealed class FlirtyQuestionTypeToolsTests
{
    /// <summary>Test double: exists only so a declaration has a validator type to carry.</summary>
    private sealed class ColourValidator : IQuestionTypeValidator
    {
        public AnswerValidationResult Validate(Question question, string value)
            => AnswerValidationResult.Valid;
    }

    private static Task<FlirtyMcpTestHost> StartWithTypesAsync()
        => FlirtyMcpTestHost.StartAsync(configureFlirty: options => options
            .AddQuestionType("address", "Postal address", """{"street":"","city":""}""")
            .AddQuestionType<ColourValidator>("color", "Colour picker", "\"#ff0000\""));

    [Fact]
    public async Task ListQuestionTypes_reports_the_declared_types_ordered_by_key()
    {
        await using var host = await StartWithTypesAsync();

        var result = await host.CallAsync<FlirtyQuestionTypeList>(FlirtyToolNames.QuestionTypeList);

        Assert.Null(result.Note);
        Assert.Collection(
            result.QuestionTypes,
            type =>
            {
                Assert.Equal("address", type.Key);
                Assert.Equal("Postal address", type.DisplayName);
                Assert.Equal("""{"street":"","city":""}""", type.Sample);
            },
            type =>
            {
                Assert.Equal("color", type.Key);
                Assert.Equal("Colour picker", type.DisplayName);
                Assert.Equal("\"#ff0000\"", type.Sample);
            });
    }

    /// <summary>
    /// The registered validator is a server-side CLR type and has no business on the wire. Asserted on the
    /// <b>serialized text</b> rather than on the projection's members, because restating the record's
    /// declaration would prove nothing: <c>System.Text.Json</c> ignores accessibility, so <c>internal</c>
    /// is no protection – every result wrapper in this package reaches the client in full.
    /// </summary>
    [Fact]
    public async Task ListQuestionTypes_does_not_put_the_validator_type_on_the_wire()
    {
        await using var host = await StartWithTypesAsync();

        var result = await host.Mcp.CallToolAsync(FlirtyToolNames.QuestionTypeList);

        var raw = result.StructuredContent!.Value.GetRawText();
        Assert.DoesNotContain("ColourValidator", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validatorType", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("color", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty case is the default host, and it must not read as a failure or a permission problem – the
    /// same reason <c>flirty_db_list_targets</c> carries a note.
    /// </summary>
    [Fact]
    public async Task ListQuestionTypes_explains_an_empty_list()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var result = await host.CallAsync<FlirtyQuestionTypeList>(FlirtyToolNames.QuestionTypeList);

        Assert.Empty(result.QuestionTypes);
        Assert.NotNull(result.Note);
        Assert.Contains("Json", result.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The round trip the tool exists for: read a key, author a question with it, get it back. Also pins
    /// that <c>customTypeKey</c> survives the create – it is a trailing optional argument, which is exactly
    /// the kind that gets dropped on the way to the command without any compiler complaint.
    /// </summary>
    [Fact]
    public async Task A_question_can_be_authored_with_a_listed_custom_type_key()
    {
        await using var host = await StartWithTypesAsync();

        var listed = await host.CallAsync<FlirtyQuestionTypeList>(FlirtyToolNames.QuestionTypeList);
        var colour = Assert.Single(listed.QuestionTypes, type => type.Key == "color");

        var dialog = await host.CreateDialogAsync("custom-types");
        var question = await host.CallAsync<QuestionDetail>(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = "colour",
                ["text"] = "Which colour?",
                ["type"] = nameof(QuestionType.Json),
                ["order"] = 0,
                ["isRequired"] = true,
                ["customTypeKey"] = colour.Key,
            });

        Assert.Equal(QuestionType.Json, question.Type);
        Assert.Equal("color", question.CustomTypeKey);

        var reloaded = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.Equal("color", Assert.Single(reloaded.Questions).CustomTypeKey);
    }

    /// <summary>
    /// The authoring guard reaches the wire: a custom type key on any type but <c>Json</c> is a 400, not a
    /// silently stored column.
    /// </summary>
    [Fact]
    public async Task A_custom_type_key_on_a_non_json_question_is_refused()
    {
        await using var host = await StartWithTypesAsync();

        var dialog = await host.CreateDialogAsync("guarded");
        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = "colour",
                ["text"] = "Which colour?",
                ["type"] = nameof(QuestionType.FreeText),
                ["order"] = 0,
                ["isRequired"] = true,
                ["customTypeKey"] = "color",
            });

        Assert.True(result.IsError);
        var problem = result.StructuredContent!.Value;
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// <c>Json</c> reaches the advertised input schema of the question tools. Pinned separately from the
    /// golden tool list, because nothing else looks at the enum constraint – and widening an input enum is
    /// the one part of this change a client can observe without authoring anything.
    /// </summary>
    [Fact]
    public async Task The_question_create_schema_offers_Json_as_a_type()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var tool = Assert.Single(
            await host.Mcp.ListToolsAsync(), candidate => candidate.Name == FlirtyToolNames.QuestionCreate);

        var values = tool.ProtocolTool.InputSchema
            .GetProperty("properties").GetProperty("type").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()).ToList();

        Assert.Contains(nameof(QuestionType.Json), values, StringComparer.Ordinal);
        Assert.Equal(Enum.GetValues<QuestionType>().Length, values.Count);
    }
}
