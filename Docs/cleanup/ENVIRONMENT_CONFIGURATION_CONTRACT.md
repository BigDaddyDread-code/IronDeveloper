# Environment Configuration Contract

**Status:** Canonical operations contract

## Required Matrix

| Profile | Required configuration |
| --- | --- |
| Developer local | `ConnectionStrings__IronDeveloperDb` and explicit `Ai__Provider`; provider-specific values when that provider is selected |
| LocalTest | database, explicit `Ai__Provider`, `LocalTest__WorkspaceRoot`, and `LocalTest__LogsRoot`; existing LocalTest isolation checks also apply |
| CI | an explicitly isolated database and explicit `Ai__Provider`; existing CI/test isolation checks also apply |
| Hosted alpha | database, explicit `Ai__Provider`, a signing key supplied through `Jwt__Key` or `IRONDEV_JWT_KEY`, and at least one `Cors__AllowedOrigins__{index}` value |
| Production-like test | database, explicit `Ai__Provider`, a signing key supplied through `Jwt__Key` or `IRONDEV_JWT_KEY`, and at least one `Cors__AllowedOrigins__{index}` value; existing production-like safety checks also apply |

`Ai__Provider` must explicitly select `Fake`, `OpenAI`, `LocalOpenAI`, or `Ollama`; missing and unknown values fail instead of taking runtime defaults. `OpenAI` requires `Ai__ApiKey` or `OPENAI_API_KEY`; `LocalOpenAI` and `Ollama` require `Ai__BaseUrl`. Per-agent custom adapters are a separate contract and do not make `Custom` a valid global API provider. When `Weaviate__Enabled=true`, `Weaviate__Endpoint` is required, and a non-boolean enabled value is rejected.

## Startup Behaviour

The API validates effective configuration before service registration. Unknown environment names, missing critical values, invalid boolean flags, unsupported providers, and known placeholders fail startup with key names and reason codes only; values and secrets are never included. The validator does not invent connection strings, signing keys, origins, endpoints, provider identities, or provider credentials.

Existing environment safety and JWT validators remain authoritative for value shape, isolated targets, and signing-key presence, provenance, and strength. Passing this contract is necessary configuration evidence, not authority to mutate, deploy, or release.
