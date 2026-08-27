using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Web.Ai;

internal static class TurnProposalJsonTranslator
{
    internal const string StructuredOutputSchemaVersion = "3.0.0";

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
          "required": ["schemaVersion", "dialogueAct", "patch", "discussionTopic"],
          "properties": {
            "schemaVersion": { "type": "integer", "const": 1 },
            "dialogueAct": {
              "type": "string",
              "enum": ["updateDraft", "discussDraft", "requestSubmission", "unrelated", "unclear"]
            },
            "patch": {
              "anyOf": [
                { "$ref": "#/$defs/patch" },
                { "type": "null" }
              ]
            },
            "discussionTopic": {
              "type": ["string", "null"],
              "enum": ["currentDraft", "missingInformation", "allowedChanges", "confirmationProcess", "resetInstructions", "unsupported", null]
            }
          },
          "$defs": {
            "patch": {
              "type": "object",
              "additionalProperties": false,
              "required": ["environment", "role", "justification", "incident"],
              "properties": {
                "environment": {
                  "anyOf": [
                    { "$ref": "#/$defs/environmentOperation" },
                    { "type": "null" }
                  ]
                },
                "role": {
                  "anyOf": [
                    { "$ref": "#/$defs/roleOperation" },
                    { "type": "null" }
                  ]
                },
                "justification": {
                  "anyOf": [
                    { "$ref": "#/$defs/justificationOperation" },
                    { "type": "null" }
                  ]
                },
                "incident": {
                  "anyOf": [
                    { "$ref": "#/$defs/incidentOperation" },
                    { "type": "null" }
                  ]
                }
              }
            },
            "environmentOperation": {
              "anyOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation"],
                  "properties": { "operation": { "type": "string", "const": "clear" } }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation", "reference"],
                  "properties": {
                    "operation": { "type": "string", "const": "set" },
                    "reference": { "$ref": "#/$defs/environmentReference" }
                  }
                }
              ]
            },
            "environmentReference": {
              "anyOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["kind", "id"],
                  "properties": {
                    "kind": { "type": "string", "const": "exactEnvironmentId" },
                    "id": { "type": "string", "minLength": 1 }
                  }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["kind", "query"],
                  "properties": {
                    "kind": { "type": "string", "const": "searchQuery" },
                    "query": { "type": "string", "minLength": 1, "maxLength": 200 }
                  }
                }
              ]
            },
            "roleOperation": {
              "anyOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation"],
                  "properties": { "operation": { "type": "string", "const": "clear" } }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation", "roleId"],
                  "properties": {
                    "operation": { "type": "string", "const": "set" },
                    "roleId": { "type": "string", "minLength": 1 }
                  }
                }
              ]
            },
            "justificationOperation": {
              "anyOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation"],
                  "properties": { "operation": { "type": "string", "const": "clear" } }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation", "value"],
                  "properties": {
                    "operation": { "type": "string", "const": "set" },
                    "value": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["text"],
                      "properties": {
                        "text": { "type": "string", "minLength": 1 }
                      }
                    }
                  }
                }
              ]
            },
            "incidentOperation": {
              "anyOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation"],
                  "properties": { "operation": { "type": "string", "const": "clear" } }
                },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["operation", "incidentId"],
                  "properties": {
                    "operation": { "type": "string", "const": "set" },
                    "incidentId": { "type": "string", "minLength": 1 }
                  }
                }
              ]
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

        public string? DiscussionTopic { get; init; }

        public TurnProposal ToProposal() => new(
            SchemaVersion,
            ParseDialogueAct(DialogueAct),
            Patch?.ToPatch(),
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

        public JustificationProposal ToProposal() => new(Text!);
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

    private static DialogueAct ParseDialogueAct(string? value) => value switch
    {
        "updateDraft" => DialogueAct.UpdateDraft,
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
