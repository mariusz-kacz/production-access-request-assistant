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
            {"schemaVersion":1,"dialogueAct":"selectClarification","clarificationSelection":{"target":"environment","optionIndex":1}}
            """,
            DialogueAct.SelectClarification
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
        """{"schemaVersion":1,"dialogueAct":"selectClarification"}""",
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
    public void ProviderSchemaIsClosedAndContainsNoModelProseChannel()
    {
        var schema = TurnProposalJsonTranslator.ProposalSchema;

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            [
                "clarificationSelection",
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
}
