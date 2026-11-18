# Contributing to ERP MCP Gateway

Thanks for your interest! Issues, ideas and pull requests are all welcome.

## Good places to start

- Issues labelled [`good first issue`](https://github.com/yasirmalik36/erp-mcp-gateway/labels/good%20first%20issue) are small and well-scoped.
- The [Roadmap](README.md#roadmap) lists bigger pieces (OAuth 2.1, ERP connectors, persistent audit store). Open an issue to discuss the design before starting one of those.
- Questions and ideas go in [Discussions](https://github.com/yasirmalik36/erp-mcp-gateway/discussions).

## Run it locally

You need the .NET 10 SDK and Node.js 20+.

```bash
# backend (demo data and demo tokens)
dotnet run --project src/ErpMcp.Server --environment Demo --urls http://localhost:5077
dotnet test

# console (proxies /api and /mcp to :5077)
cd web && npm ci && npx ng serve
```

Or run everything with Docker: `docker build -t erp-mcp-gateway . && docker run -p 8080:8080 erp-mcp-gateway`.

## Pull request checklist

- `dotnet build` passes with no warnings (warnings are treated as errors).
- `dotnet test` passes, and new behaviour has a test in `tests/ErpMcp.Tests`.
- Console changes work in both the light and dark theme.
- Keep the security model intact: role checks, masking, human approval for writes and the audit chain. If a change touches one of these, explain why in the PR.
- Use fictional data only. Never commit real company data, IBANs or credentials.

## Reporting a security issue

Please don't open a public issue for a vulnerability. Use GitHub's
[private vulnerability reporting](https://github.com/yasirmalik36/erp-mcp-gateway/security/advisories/new) instead.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
