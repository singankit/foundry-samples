# Foundry A365 Echo Agent

A minimal Foundry A365 (digital worker) agent that echoes back any message sent to it. This sample demonstrates the bare minimum infrastructure needed to deploy a hosted agent without any AI/LLM calls.

## What This Sample Does

- Deploys a hosted container agent to Azure AI Foundry
- Registers it as a digital worker in Microsoft 365
- Echoes back any message received via the Activity Protocol

## Prerequisites

- Azure subscription
- Azure Developer CLI (`azd`)
- .NET 9 SDK
- PowerShell 7+

## Deploy

```bash
azd init
azd provision
```

## Post-Deployment

1. **Approve blueprint** in [M365 Admin Center](https://admin.cloud.microsoft/?#/agents/all/requested)
2. **Configure Bot ID** in [Teams Developer Portal](https://dev.teams.microsoft.com/tools/agent-blueprint) → use Blueprint Client ID from `azd env get-values`
3. **Create agent instance** in Teams → Apps → Agents for your team

## Architecture

```
Teams → Activity Protocol → Foundry Hosted Container → Echo Response
```

No AI models, no MCP tools, no token acquisition — just a direct echo.
