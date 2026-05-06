"""A365 observability bootstrap — must be imported before any framework code.

Uses microsoft-opentelemetry distro instead of the standalone A365 observability SDK.
"""

import logging
import os

from dotenv import load_dotenv

load_dotenv(override=False)

logger = logging.getLogger(__name__)


# A365 trace ingestion resource appId. Override only if your tenant routes elsewhere.
A365_INGESTION_RESOURCE_APP_ID = os.environ.get(
    "A365_INGESTION_RESOURCE_APP_ID",
    "9b975845-388f-4429-889e-eab1ef63949c",
)
A365_SCOPE = f"api://{A365_INGESTION_RESOURCE_APP_ID}/.default"


def setup() -> None:
    """Configure A365 observability via the microsoft-opentelemetry distro."""
    from azure.identity import DefaultAzureCredential
    from microsoft.opentelemetry import use_microsoft_opentelemetry

    os.environ.setdefault("ENABLE_A365_OBSERVABILITY_EXPORTER", "true")

    # The distro's Foundry agent ID override is controlled via env vars:
    #   FOUNDRY_HOSTING_ENVIRONMENT=1  — enables the override
    #   FOUNDRY_AGENT_IDENTITY=<appId> — the agent identity SP's appId
    # Set them here if not already set by the hosting platform.
    os.environ.setdefault("FOUNDRY_HOSTING_ENVIRONMENT", "1")
    agent_app_id = os.environ.get("AGENT_IDENTITY_APP_ID", "")
    if agent_app_id:
        os.environ.setdefault("FOUNDRY_AGENT_IDENTITY", agent_app_id)

    credential = DefaultAzureCredential()

    def token_resolver(agent_id: str, tenant_id: str) -> str | None:
        try:
            return credential.get_token(A365_SCOPE).token
        except Exception as exc:
            print("Failed to resolve A365 token:", exc, file=os.sys.stderr)
            logger.error("A365 token resolution failed: %s", exc)
            return None

    use_microsoft_opentelemetry(
        enable_azure_monitor=True,
        enable_a365=True,
        a365_token_resolver=token_resolver,
        a365_cluster_category="prod",
        a365_use_s2s_endpoint=True,
        a365_enable_observability_exporter=True,
    )
