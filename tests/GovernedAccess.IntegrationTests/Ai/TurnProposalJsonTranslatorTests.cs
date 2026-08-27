using System.Text.Json;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class TurnProposalJsonTranslatorTests
{
    public static TheoryData<string, DialogueAct> SupportedPayloads => new()
    {
        {
            """
            {"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"role":{"operation":"clear"}}}
            """,
            DialogueAct.UpdateDraft
        },
        {
            """
            {"schemaVersion":1,"dialogueAct":"discussDraft","discussionTopic":"currentDraft"}
            """,
            DialogueAct.DiscussDraft
        },
        {
            """{"schemaVersion":1,"dialogueAct":"requestSubmission"}""",
            DialogueAct.RequestSubmission
        },
        {
            """{"schemaVersion":1,"dialogueAct":"unrelated"}""",
            DialogueAct.Unrelated
        },
        {
            """{"schemaVersion":1,"dialogueAct":"unclear"}""",
            DialogueAct.Unclear
        },
    };

    public static TheoryData<string> StructurallyInvalidPayloads => new()
    {
        """{"schemaVersion":2,"dialogueAct":"unclear"}""",
        """{"schemaVersion":1,"dialogueAct":"unknown"}""",
        """{"schemaVersion":1,"dialogueAct":"updateDraft"}""",
        """{"schemaVersion":1,"dialogueAct":"updateDraft","patch":{}}""",
        """{"schemaVersion":1,"dialogueAct":"unclear","patch":{"role":{"operation":"clear"}}}""",
        """{"schemaVersion":1,"dialogueAct":"discussDraft","discussionTopic":"invented"}""",
        """{"schemaVersion":1,"dialogueAct":"requestSubmission","discussionTopic":"currentDraft"}""",
        """{"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"role":{"operation":"keep"}}}""",
        """{"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"role":{"operation":"clear","roleId":"ProductionReadOnly"}}}""",
        """{"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"role":{"operation":"set"}}}""",
        """{"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"environment":{"operation":"set","reference":{"kind":"searchQuery","id":"PROD-ALPHA-EU","query":"alpha"}}}}""",
        """{"schemaVersion":1,"dialogueAct":"unclear","modelProse":"approved"}""",
    };

    [Theory]
    [MemberData(nameof(SupportedPayloads))]
    public void TranslatesEveryClosedDialogueAct(
        string payload,
        DialogueAct expectedAct)
    {
        var translated = TurnProposalJsonTranslator.TryTranslate(
            payload,
            out var proposal);

        Assert.True(translated);
        Assert.Equal(expectedAct, Assert.IsType<TurnProposal>(proposal).DialogueAct);
    }

    [Theory]
    [MemberData(nameof(StructurallyInvalidPayloads))]
    public void RejectsStructuralContractViolations(string payload)
    {
        Assert.False(TurnProposalJsonTranslator.TryTranslate(payload, out var proposal));
        Assert.Null(proposal);
    }

    [Fact]
    public void TranslatesStrictProviderPayloadWithExplicitNulls()
    {
        const string payload =
            """
            {"schemaVersion":1,"dialogueAct":"unclear","patch":null,"discussionTopic":null}
            """;

        var translated = TurnProposalJsonTranslator.TryTranslate(
            payload,
            out var proposal);

        Assert.True(translated);
        Assert.Equal(DialogueAct.Unclear, proposal!.DialogueAct);
        Assert.Null(proposal.Patch);
        Assert.Null(proposal.DiscussionTopic);
    }

    [Fact]
    public void ProviderSchemaIsClosedAndContainsNoModelProseChannel()
    {
        var schema = TurnProposalJsonTranslator.ProposalSchema;

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            [
                "dialogueAct",
                "discussionTopic",
                "patch",
                "schemaVersion",
            ],
            schema.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            "prose",
            schema.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
        AssertAllObjectSchemasAreClosed(schema);
    }

    [Fact]
    public void ProviderSchemaUsesFoundryStrictObjectShape()
    {
        var schema = TurnProposalJsonTranslator.ProposalSchema;

        Assert.DoesNotContain(
            "\"minProperties\"",
            schema.GetRawText(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"oneOf\"",
            schema.GetRawText(),
            StringComparison.Ordinal);
        AssertAllObjectPropertiesAreRequired(schema);
        AssertAllConstantsDeclareType(schema);

        var rootProperties = schema.GetProperty("properties");
        AssertAllowsNull(rootProperties.GetProperty("patch"));
        AssertAllowsNull(rootProperties.GetProperty("discussionTopic"));

        var patchProperties = schema
            .GetProperty("$defs")
            .GetProperty("patch")
            .GetProperty("properties");
        foreach (var property in patchProperties.EnumerateObject())
        {
            AssertAllowsNull(property.Value);
        }
    }

    [Fact]
    public void TextOnlyJustificationTranslatesThroughTheClosedProviderSchema()
    {
        const string payload =
            """
            {"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"justification":{"operation":"set","value":{"text":"Investigate elevated customer errors."}}}}
            """;

        var translated = TurnProposalJsonTranslator.TryTranslate(
            payload,
            out var proposal);

        Assert.True(translated);
        var patch = Assert.IsType<DraftPatch>(proposal!.Patch);
        Assert.Equal(
            "Investigate elevated customer errors.",
            Assert.IsType<SetJustificationOperation>(patch.Justification).Value.Text);
        var valueSchema = TurnProposalJsonTranslator.ProposalSchema
            .GetProperty("$defs")
            .GetProperty("justificationOperation")
            .GetProperty("anyOf")[1]
            .GetProperty("properties")
            .GetProperty("value");
        Assert.Equal(
            ["text"],
            valueSchema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.Equal(
            ["text"],
            valueSchema.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name));
    }

    private static void AssertAllObjectSchemasAreClosed(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "object")
            {
                Assert.True(element.TryGetProperty("additionalProperties", out var additional));
                Assert.False(additional.GetBoolean());
            }

            foreach (var property in element.EnumerateObject())
            {
                AssertAllObjectSchemasAreClosed(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertAllObjectSchemasAreClosed(item);
            }
        }
    }

    private static void AssertAllObjectPropertiesAreRequired(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "object")
            {
                var properties = element
                    .GetProperty("properties")
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal);
                var required = element
                    .GetProperty("required")
                    .EnumerateArray()
                    .Select(item => item.GetString())
                    .Order(StringComparer.Ordinal);
                Assert.Equal(properties, required);
            }

            foreach (var property in element.EnumerateObject())
            {
                AssertAllObjectPropertiesAreRequired(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertAllObjectPropertiesAreRequired(item);
            }
        }
    }

    private static void AssertAllowsNull(JsonElement element)
    {
        var allowsNull = element.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.Array
            && type.EnumerateArray().Any(
                item => item.ValueKind == JsonValueKind.String
                    && item.GetString() == "null");
        allowsNull |= element.TryGetProperty("anyOf", out var anyOf)
            && anyOf.EnumerateArray().Any(
                item => item.TryGetProperty("type", out var itemType)
                    && itemType.ValueKind == JsonValueKind.String
                    && itemType.GetString() == "null");

        Assert.True(allowsNull, "The schema must represent optional values with null.");
    }

    private static void AssertAllConstantsDeclareType(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("const", out _))
            {
                Assert.True(
                    element.TryGetProperty("type", out _),
                    "Every constant schema must declare its type.");
            }

            foreach (var property in element.EnumerateObject())
            {
                AssertAllConstantsDeclareType(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertAllConstantsDeclareType(item);
            }
        }
    }
}
