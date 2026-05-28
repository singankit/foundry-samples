# 🤖 Foundry A365 Echo Agent

> A minimal example of deploying a Foundry A365 echo agent with Azure Developer CLI.

---

## 📋 Prerequisites

**Note:** You must be enrolled in the [Frontier preview program](https://adoption.microsoft.com/en-us/copilot/frontier-program/) to publish a Foundry agent to Microsoft Agent 365.

Ensure you have the following installed:

| Requirement | Description |
|-------------|-------------|
| [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) | Infrastructure deployment tool |
| [.NET 9.0 SDK](https://dotnet.microsoft.com/download) | Development framework |
| [PowerShell 7+](https://learn.microsoft.com/powershell/) | Deployment scripts |
| Docker Desktop | Container build and push |

### 🔐 Required Permissions

- **Owner** role on the Azure subscription
- **Foundry User** or **Cognitive Services User** role at subscription or resource group level
- **Tenant Admin** role for organization-wide configuration

---

## 🚀 Quick Start

### Step 1: Authenticate

Login to your Azure tenant and authenticate with Azure Developer CLI. Depending on your tenant's security settings, `az login` alone may be sufficient, or you may need to additionally sign in for the specific scopes used by the deployment scripts.

```powershell
# Login to Azure CLI
az login

# Login to Azure Developer CLI
azd auth login
```

### Step 2: Deploy Everything

> **📍 Region availability:** This sample uses [Foundry hosted agents](https://learn.microsoft.com/en-us/azure/foundry/agents/quickstarts/quickstart-hosted-agent?pivots=azd). Your Foundry account and related resources must be in a supported region. At the time of writing, supported regions are:
>
> Australia East, Brazil South, Canada Central, Canada East, East US, East US 2, France Central, Germany West Central, Italy North, Japan East, Korea Central, North Central US, Norway East, Poland Central, South Africa North, South Central US, South India, Southeast Asia, Spain Central, Sweden Central, Switzerland North, UAE North, UK South, West Central US, West US, West US 3.

Ensure Docker is running, then execute:

```powershell
azd provision
```

After deployment completes, retrieve your resource values:

```powershell
azd env get-values
```

> **📌 What to expect after deployment:**  
> After `azd provision` completes successfully, you will see the **AgentIdentityBlueprint** in the Agents registry. You will **not** see any agents in the requests tab yet. This is expected - you must first approve the agent blueprint, configure it in Teams Developer Portal, and then create agent instances based on that blueprint.

### Step 3: Approve the Agent Blueprint

The first step is to approve the **agent blueprint** itself. Agent instances are created later.

1. Open the [Microsoft 365 admin center](https://admin.cloud.microsoft/?#/agents/all/requested)
2. Under **Requests**, locate your agent blueprint
3. Select **Approve request and activate**

### Step 4: Configure Teams Integration

After approval, configure the blueprint in Teams Developer Portal:

1. Open the [Teams Developer Portal](https://dev.teams.microsoft.com/tools/agent-blueprint)
2. Locate your approved blueprint (or navigate directly by ID)
3. Run `azd env get-values` and copy your Blueprint ID
4. In **Configuration**, set **Bot ID** to that same Blueprint ID

### Step 5: Create Agent Instances

1. In Microsoft Teams, open **Apps** → **Agents for your team**
2. Find your approved blueprint and create an instance

---

## 🏗️ Architecture

```
Teams → Activity Protocol → Foundry Hosted Container → Echo Response
```

This sample deploys:

1. Foundry account + project and Azure Container Registry
2. Managed agent identity blueprint
3. Bot Service wired to the hosted agent endpoint
4. Hosted echo agent container image and agent registration
5. Digital worker publication for Microsoft 365

---

## 📜 Hosted Agent Logs

If you receive an error, the response may include a `FOUNDRY_AGENT_SESSION_ID`. Use it to stream hosted agent logs:

```bash
curl -N \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: text/event-stream" \
  -H "Cache-Control: no-cache" \
  -H "Foundry-Features: HostedAgents=V1Preview" \
  "https://$ACCOUNT_NAME.services.ai.azure.com/api/projects/$PROJECT_NAME/agents/$AGENT_NAME/sessions/$SESSION_NAME:logstream?api-version=2025-11-15-preview"
```
