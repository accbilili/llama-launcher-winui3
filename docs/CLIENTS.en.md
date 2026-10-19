# Client setup

[简体中文](CLIENTS.md) · [Back to README](../README.en.md)

Start the model and wait for the server-ready status before opening **客户端接入** (Client setup). Connection information prioritizes the running server's port, model ID, and actual context. Selecting another model in the sidebar does not change the running server.

The examples use port `8081`. The `local` API key is a nonempty placeholder; the launched local server currently does not enable key authentication.

| Client | Address | Protocol |
| --- | --- | --- |
| WorkBuddy | `http://127.0.0.1:8081/v1/chat/completions` | Custom protocol, full endpoint |
| Cherry Studio | `http://127.0.0.1:8081/v1` | OpenAI / Chat Completions |
| DeepSeek Harness | `http://127.0.0.1:8081/v1` | Custom model API / OpenAI Chat Completions |

## WorkBuddy

1. Select WorkBuddy and review the URL, model ID, and token budgets.
2. Click **写入 WorkBuddy** (Write to WorkBuddy). The launcher merges into `%USERPROFILE%\.workbuddy\models.json`, backing up existing content before writing.
3. Reselect the generated local model in WorkBuddy. Restart the client or create a new task if necessary.
4. For manual setup, enable custom protocol and use the full `/v1/chat/completions` endpoint.

Writes preserve other models and unknown fields in the target entry. Malformed JSON and conflicting non-local entries with the same ID are rejected. Exported JSON is a single-model snippet; do not use it to replace the entire configuration list.

The backup area can refresh the list, open its folder, and restore a selected backup. Restoration first backs up the current configuration, then writes the selected backup's original bytes. Invalid or unrelated backups are rejected.

## Cherry Studio

1. Click **导入 Cherry Studio** (Import into Cherry Studio), then confirm in Cherry's own provider-import dialog.
2. Fetch/add the model in that provider using the exact ID shown by the launcher.
3. Select the provider and model in chat.

The native link imports provider settings, not the model list. After switching the model on the same port, refresh and reselect it. The link requires Cherry Studio to have registered the `cherrystudio` protocol. Use Chat Completions, not an Ollama or Responses provider.

## DeepSeek Harness

1. Click **复制 Harness 填写信息** (Copy Harness setup information).
2. In Harness's native model settings, add a custom API provider using the displayed Provider ID, base URL, OpenAI Chat Completions, and `local` key.
3. Fetch/add the model, set context and maximum output from the displayed budgets, then select it for a new conversation.

Alternatively, export the YAML snippet and manually merge it into the active `llm-pi-ai` configuration. The launcher does not rewrite YAML automatically. Use Harness itself to locate the active configuration; menu names and protocol fields depend on the installed client version.

## Checks and troubleshooting

- **Check connection** reads only `/health` and `/v1/models`.
- Basic, streaming, and tool checks make inference requests, support cancellation, and have a three-minute timeout. The tool only echoes `ping`; it has no external executor.
- **404:** Check the port, path, and protocol. WorkBuddy uses a full endpoint; the other clients use a base URL. Avoid duplicating `/v1`.
- **Unavailable model:** Compare the model ID with `/v1/models`, reselect it after switching models, and try a new task.
- **Connection refused:** Confirm that the server is ready, the launcher has not exited, and the address points to the machine hosting the server.
- **Reasoning with no answer:** Check the output budget, adjust thinking settings, and restart the server. A template declaration is not runtime verification.

These checks validate local service interfaces, not an entire third-party Agent workflow. Test tool use, vision, long contexts, and complex tasks separately.
