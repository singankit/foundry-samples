# Copyright (c) Microsoft. All rights reserved.

import logging
import os

# Enable DEBUG logging for the OTel distro and HTTP transport to diagnose
# outgoing trace export requests to A365.
logging.basicConfig(level=logging.DEBUG)
logging.getLogger("microsoft.opentelemetry").setLevel(logging.DEBUG)
logging.getLogger("opentelemetry").setLevel(logging.DEBUG)
logging.getLogger("opentelemetry.exporter").setLevel(logging.DEBUG)
logging.getLogger("azure.core.pipeline").setLevel(logging.DEBUG)
logging.getLogger("urllib3").setLevel(logging.DEBUG)
logging.getLogger("azure.ai.agentserver").setLevel(logging.DEBUG)

from starlette.middleware import Middleware
from starlette.requests import Request
from starlette.types import ASGIApp, Receive, Scope, Send
from agent_framework import Agent
from agent_framework.foundry import FoundryChatClient
from agent_framework_foundry_hosting import ResponsesHostServer
from azure.identity import DefaultAzureCredential
from dotenv import load_dotenv
from opentelemetry import trace, baggage, context as otel_context
from opentelemetry.sdk.trace import SpanProcessor


class DebugBaggageSpanProcessor(SpanProcessor):
    """Logs baggage contents when each span starts to diagnose propagation."""

    def on_start(self, span, parent_context=None):
        ctx = parent_context or otel_context.get_current()
        baggage_map = baggage.get_all(ctx)
        span_name = span.name if hasattr(span, 'name') else 'unknown'
        logging.error(f"=== DEBUG SPAN START: {span_name} ===")
        logging.error(f"  Baggage entries: {dict(baggage_map)}")
        logging.error(f"  Has user.id: {'user.id' in baggage_map}")
        if 'user.id' in baggage_map:
            logging.error(f"  user.id value: {baggage_map['user.id']}")

    def on_end(self, span):
        pass

    def shutdown(self):
        pass

    def force_flush(self, timeout_millis=None):
        return True


class BaggageInjectionMiddleware:
    """ASGI middleware that injects user.id into the W3C baggage header
    before the endpoint handler extracts it into OTel context."""

    def __init__(self, app: ASGIApp):
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send):
        if scope["type"] == "http":
            headers = list(scope.get("headers", []))
            new_headers = []
            for k, v in headers:
                if k == b"baggage":
                    # Append user.id to existing baggage
                    v = v + b",user.id=fd50db2c-2ab7-415d-8f1d-2e66a7c71e54"
                    logging.error(f"=== MODIFIED BAGGAGE: {v.decode()} ===")
                new_headers.append((k, v))
            scope["headers"] = new_headers
        await self.app(scope, receive, send)

# Load environment variables from .env file
load_dotenv()


def test_a365_token():
    """Test fetching a token for A365 observability scope."""
    try:
        credential = DefaultAzureCredential()
        token = credential.get_token("api://9b975845-388f-4429-889e-eab1ef63949c/.default")
        print("A365 token acquired successfully.", file=os.sys.stderr)
        logging.error(f"A365 token acquired successfully. Expires on: {token.expires_on}")
    except Exception as e:
        print(f"Failed to acquire A365 token: {e}", file=os.sys.stderr)
        logging.error(f"Failed to acquire A365 token: {e}")


def print_all_env_vars():
    """Print all environment variables for debugging."""
    logging.info("=== ALL ENVIRONMENT VARIABLES ===")
    for key, value in sorted(os.environ.items()):
        logging.info(f"  {key}={value}")
        print(f"{key}={value}", file=os.sys.stderr)
    logging.info("=== END ENVIRONMENT VARIABLES ===")


def main():
    print_all_env_vars()
    test_a365_token()

    client = FoundryChatClient(
        project_endpoint=os.environ["FOUNDRY_PROJECT_ENDPOINT"],
        model=os.environ["AZURE_AI_MODEL_DEPLOYMENT_NAME"],
        credential=DefaultAzureCredential(),
    )

    agent = Agent(
        client=client,
        instructions="You are a friendly assistant. Keep your answers brief.",
        # History will be managed by the hosting infrastructure, thus there
        # is no need to store history by the service. Learn more at:
        # https://developers.openai.com/api/reference/resources/responses/methods/create
        default_options={"store": False},
    )

    server = ResponsesHostServer(agent)

    # Add baggage injection middleware to inject user.id before OTel extraction
    server.add_middleware(BaggageInjectionMiddleware)

    # Register debug span processor to log baggage on every span start
    from opentelemetry.sdk.trace import TracerProvider as SdkTracerProvider
    provider = trace.get_tracer_provider()
    # The provider might be wrapped; try to get the underlying SDK provider
    actual_provider = getattr(provider, '_proxy_tracer_provider', provider)
    if not isinstance(actual_provider, SdkTracerProvider):
        actual_provider = getattr(actual_provider, '_real_tracer_provider', actual_provider)
    if isinstance(actual_provider, SdkTracerProvider):
        actual_provider.add_span_processor(DebugBaggageSpanProcessor())
        logging.error("=== DebugBaggageSpanProcessor registered successfully ===")
    else:
        logging.error(f"=== Could not register DebugBaggageSpanProcessor, provider type: {type(actual_provider)} ===")

    server.run()


if __name__ == "__main__":
    main()
