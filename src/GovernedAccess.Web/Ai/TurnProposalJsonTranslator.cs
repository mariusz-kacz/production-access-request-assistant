using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Web.Ai;

internal static class TurnProposalJsonTranslator
{
    internal const string StructuredOutputSchemaVersion = "1.0.0";

    internal static JsonSerializerOptions SerializerOptions { get; } =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

    internal static JsonElement ProposalSchema { get; } = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["schemaVersion", "dialogueAct"],
          "properties": {
            "schemaVersion": { "type": "integer", "const": 1 },
            "dialogueAct": {
              "type": "string",
              "enum": ["updateDraft", "selectClarification", "discussDraft", "requestSubmission", "unrelated", "unclear"]
            },
            "patch": { "$ref": "#/$defs/patch" },
            "clarificationSelection": { "$ref": "#/$defs/clarificationSelection" },
            "discussionTopic": {
              "type": "string",
              "enum": ["currentDraft", "missingInformation", "allowedChanges", "confirmationProcess", "resetInstructions", "unsupported"]
            }
          },
          "$defs": {
            "patch": {
              "type": "object",
              "additionalProperties": false,
              "minProperties": 1,
              "properties": {
                "environment": { "$ref": "#/$defs/environmentOperation" },
                "role": { "$ref": "#/$defs/roleOperation" },
                "justification": { "$ref": "#/$defs/justificationOperation" },
                "incident": { "$ref": "#/$defs/incidentOperation" }
              }
            },
            "environmentOperation": {
              "oneOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation"],
                  "properties": { "operation": { "const": "clear" } }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation", "reference"],
                  "properties": {
                    "operation": { "const": "set" },
                    "reference": { "$ref": "#/$defs/environmentReference" }
                  }
                }
              ]
            },
            "environmentReference": {
              "oneOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["kind", "id"],
                  "properties": {
                    "kind": { "const": "exactEnvironmentId" },
                    "id": { "type": "string", "minLength": 1 }
                  }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["kind", "query"],
                  "properties": {
                    "kind": { "const": "searchQuery" },
                    "query": { "type": "string", "minLength": 1, "maxLength": 200 }
                  }
                }
              ]
            },
            "roleOperation": {
              "oneOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation"],
                  "properties": { "operation": { "const": "clear" } }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation", "roleId"],
                  "properties": {
                    "operation": { "const": "set" },
                    "roleId": { "type": "string", "minLength": 1 }
                  }
                }
              ]
            },
            "justificationOperation": {
              "oneOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation"],
                  "properties": { "operation": { "const": "clear" } }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation", "value"],
                  "properties": {
                    "operation": { "const": "set" },
                    "value": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["text", "provenance"],
                      "properties": {
                        "text": { "type": "string", "minLength": 1 },
                        "provenance": { "const": "requesterAuthoredNormalized" }
                      }
                    }
                  }
                }
              ]
            },
            "incidentOperation": {
              "oneOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation"],
                  "properties": { "operation": { "const": "clear" } }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation", "incidentId"],
                  "properties": {
                    "operation": { "const": "set" },
                    "incidentId": { "type": "string", "minLength": 1 }
                  }
                }
              ]
            },
            "clarificationSelection": {
              "type": "object",
              "additionalProperties": false,
              "required": ["target", "optionIndex"],
              "properties": {
                "target": { "type": "string", "enum": ["environment", "role"] },
                "optionIndex": { "type": "integer", "minimum": 1 }
              }
            }
          }
        }
        """).RootElement.Clone();

    internal static bool TryTranslate(string responseText, out TurnProposal? proposal)
    {
        proposal = null;
        try
        {
            var payload = JsonSerializer.Deserialize<ProposalPayload>(
                responseText,
                SerializerOptions);
            proposal = payload?.ToProposal();
            return proposal is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class ProposalPayload
    {
        [JsonRequired]
        public int SchemaVersion { get; init; }

        [JsonRequired]
        public string? DialogueAct { get; init; }

        public PatchPayload? Patch { get; init; }

        public ClarificationSelectionPayload? ClarificationSelection { get; init; }

        public string? DiscussionTopic { get; init; }

        public TurnProposal ToProposal() => new(
            SchemaVersion,
            ParseDialogueAct(DialogueAct),
            Patch?.ToPatch(),
            ClarificationSelection?.ToSelection(),
            ParseDiscussionTopic(DiscussionTopic));
    }

    private sealed class PatchPayload
    {
        public EnvironmentOperationPayload? Environment { get; init; }
        public RoleOperationPayload? Role { get; init; }
        public JustificationOperationPayload? Justification { get; init; }
        public IncidentOperationPayload? Incident { get; init; }

        public DraftPatch ToPatch() => new(
            Environment?.ToOperation(),
            Role?.ToOperation(),
            Justification?.ToOperation(),
            Incident?.ToOperation());
    }

    private sealed class EnvironmentOperationPayload
    {
        [JsonRequired]
        public string? Operation { get; init; }
        public EnvironmentReferencePayload? Reference { get; init; }

        public EnvironmentOperation ToOperation() => Operation switch
        {
            "clear" when Reference is null => new ClearEnvironmentOperation(),
            "set" when Reference is not null =>
                new SetEnvironmentOperation(Reference.ToReference()),
            _ => throw new JsonException("Invalid environment operation."),
        };
    }

    private sealed class EnvironmentReferencePayload
    {
        [JsonRequired]
        public string? Kind { get; init; }
        public string? Id { get; init; }
        public string? Query { get; init; }

        public EnvironmentReference ToReference() => Kind switch
        {
            "exactEnvironmentId" when Query is null => new ExactEnvironmentId(Id!),
            "searchQuery" when Id is null => new EnvironmentSearchQuery(Query!),
            _ => throw new JsonException("Invalid environment reference."),
        };
    }

    private sealed class RoleOperationPayload
    {
        [JsonRequired]
        public string? Operation { get; init; }
        public string? RoleId { get; init; }

        public RoleOperation ToOperation() => Operation switch
        {
            "clear" when RoleId is null => new ClearRoleOperation(),
            "set" => new SetRoleOperation(RoleId!),
            _ => throw new JsonException("Invalid role operation."),
        };
    }

    private sealed class JustificationOperationPayload
    {
        [JsonRequired]
        public string? Operation { get; init; }
        public JustificationPayload? Value { get; init; }

        public JustificationOperation ToOperation() => Operation switch
        {
            "clear" when Value is null => new ClearJustificationOperation(),
            "set" when Value is not null =>
                new SetJustificationOperation(Value.ToProposal()),
            _ => throw new JsonException("Invalid justification operation."),
        };
    }

    private sealed class JustificationPayload
    {
        [JsonRequired]
        public string? Text { get; init; }
        [JsonRequired]
        public string? Provenance { get; init; }

        public JustificationProposal ToProposal() => new(
            Text!,
            Provenance == "requesterAuthoredNormalized"
                ? JustificationProvenance.RequesterAuthoredNormalized
                : throw new JsonException("Invalid justification provenance."));
    }

    private sealed class IncidentOperationPayload
    {
        [JsonRequired]
        public string? Operation { get; init; }
        public string? IncidentId { get; init; }

        public IncidentOperation ToOperation() => Operation switch
        {
            "clear" when IncidentId is null => new ClearIncidentOperation(),
            "set" => new SetIncidentOperation(IncidentId!),
            _ => throw new JsonException("Invalid incident operation."),
        };
    }

    private sealed class ClarificationSelectionPayload
    {
        [JsonRequired]
        public string? Target { get; init; }
        [JsonRequired]
        public int OptionIndex { get; init; }

        public ClarificationSelection ToSelection() => new(
            Target switch
            {
                "environment" => ClarificationTarget.Environment,
                "role" => ClarificationTarget.Role,
                _ => throw new JsonException("Invalid clarification target."),
            },
            OptionIndex);
    }

    private static DialogueAct ParseDialogueAct(string? value) => value switch
    {
        "updateDraft" => DialogueAct.UpdateDraft,
        "selectClarification" => DialogueAct.SelectClarification,
        "discussDraft" => DialogueAct.DiscussDraft,
        "requestSubmission" => DialogueAct.RequestSubmission,
        "unrelated" => DialogueAct.Unrelated,
        "unclear" => DialogueAct.Unclear,
        _ => throw new JsonException("Invalid dialogue act."),
    };

    private static DiscussionTopic? ParseDiscussionTopic(string? value) => value switch
    {
        null => null,
        "currentDraft" => DiscussionTopic.CurrentDraft,
        "missingInformation" => DiscussionTopic.MissingInformation,
        "allowedChanges" => DiscussionTopic.AllowedChanges,
        "confirmationProcess" => DiscussionTopic.ConfirmationProcess,
        "resetInstructions" => DiscussionTopic.ResetInstructions,
        "unsupported" => DiscussionTopic.Unsupported,
        _ => throw new JsonException("Invalid discussion topic."),
    };
}
