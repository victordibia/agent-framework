# Copyright (c) Microsoft. All rights reserved.

"""OpenAI Executor - proxies requests to OpenAI Responses API.

This executor mirrors the AgentFrameworkExecutor interface but routes
requests to OpenAI's API instead of executing local entities.
"""

import logging
import os
from collections.abc import AsyncGenerator
from typing import Any

from openai import AsyncOpenAI, AsyncStream
from openai.types.responses import Response, ResponseStreamEvent

from .._conversations import ConversationStore
from ..models import AgentFrameworkRequest, OpenAIError, OpenAIResponse

logger = logging.getLogger(__name__)


class OpenAIExecutor:
    """Executor for OpenAI Responses API - mirrors AgentFrameworkExecutor interface.

    This executor provides the same interface as AgentFrameworkExecutor but proxies
    requests to OpenAI's Responses API instead of executing local entities.

    Key features:
    - Same execute_streaming() and execute_sync() interface
    - Shares ConversationStore with local executor
    - Configured via OPENAI_API_KEY environment variable
    - Supports all OpenAI Responses API parameters
    """

    def __init__(self, conversation_store: ConversationStore):
        """Initialize OpenAI executor.

        Args:
            conversation_store: Shared conversation store (works for both local and OpenAI)
        """
        self.conversation_store = conversation_store

        # Load configuration from environment
        self.api_key = os.getenv("OPENAI_API_KEY")
        self.base_url = os.getenv("OPENAI_BASE_URL", "https://api.openai.com/v1")
        self._client: AsyncOpenAI | None = None

    @property
    def is_configured(self) -> bool:
        """Check if OpenAI executor is properly configured.

        Returns:
            True if OPENAI_API_KEY is set
        """
        return self.api_key is not None

    def _get_client(self) -> AsyncOpenAI:
        """Get or create OpenAI async client.

        Returns:
            AsyncOpenAI client instance

        Raises:
            ValueError: If OPENAI_API_KEY not configured
        """
        if self._client is None:
            if not self.api_key:
                raise ValueError("OPENAI_API_KEY environment variable not set")

            self._client = AsyncOpenAI(
                api_key=self.api_key,
                base_url=self.base_url,
            )
            logger.debug(f"Created OpenAI client with base_url: {self.base_url}")

        return self._client

    async def execute_streaming(self, request: AgentFrameworkRequest) -> AsyncGenerator[Any, None]:
        """Execute request via OpenAI and stream results in OpenAI format.

        This mirrors AgentFrameworkExecutor.execute_streaming() interface.

        Args:
            request: Request to execute

        Yields:
            OpenAI ResponseStreamEvent objects (already in correct format!)
        """
        if not self.is_configured:
            logger.error("OpenAI executor not configured (missing OPENAI_API_KEY)")
            error = OpenAIError.create("OpenAI not configured on server. Set OPENAI_API_KEY environment variable.")
            yield error.to_dict()
            return

        try:
            client = self._get_client()

            # Convert AgentFrameworkRequest to OpenAI params
            params = request.to_openai_params()

            # Remove DevUI-specific fields that OpenAI doesn't recognize
            params.pop("extra_body", None)

            # Conversation ID is now from OpenAI (created via /v1/conversations proxy)
            # so we can pass it through!

            # Force streaming mode (remove if already present to avoid duplicate)
            params.pop("stream", None)

            logger.info(f"🔀 Proxying to OpenAI Responses API: model={params.get('model')}")
            logger.debug(f"Request params: {params}")

            # Call OpenAI Responses API - returns AsyncStream[ResponseStreamEvent]
            stream: AsyncStream[ResponseStreamEvent] = await client.responses.create(
                **params,
                stream=True,  # Force streaming
            )

            # Yield events directly - they're already ResponseStreamEvent objects!
            # No conversion needed - OpenAI SDK returns proper typed objects
            async for event in stream:
                yield event

        except Exception as e:
            logger.error(f"OpenAI proxy error: {e}", exc_info=True)
            error = OpenAIError.create(f"OpenAI API error: {e!s}")
            yield error.to_dict()

    async def execute_sync(self, request: AgentFrameworkRequest) -> OpenAIResponse:
        """Execute request via OpenAI and return complete response.

        This mirrors AgentFrameworkExecutor.execute_sync() interface.

        Args:
            request: Request to execute

        Returns:
            Final OpenAI Response object

        Raises:
            ValueError: If OpenAI not configured
            Exception: If OpenAI API call fails
        """
        if not self.is_configured:
            raise ValueError("OpenAI not configured on server. Set OPENAI_API_KEY environment variable.")

        try:
            client = self._get_client()

            # Convert AgentFrameworkRequest to OpenAI params
            params = request.to_openai_params()

            # Remove DevUI-specific fields
            params.pop("extra_body", None)

            # Force non-streaming mode (remove if already present to avoid duplicate)
            params.pop("stream", None)

            logger.info(f"🔀 Proxying to OpenAI Responses API (non-streaming): model={params.get('model')}")
            logger.debug(f"Request params: {params}")

            # Call OpenAI Responses API - returns Response object
            response: Response = await client.responses.create(
                **params,
                stream=False,  # Force non-streaming
            )

            return response

        except Exception as e:
            logger.error(f"OpenAI proxy error: {e}", exc_info=True)
            raise

    async def close(self) -> None:
        """Close the OpenAI client and release resources."""
        if self._client:
            await self._client.close()
            self._client = None
            logger.debug("Closed OpenAI client")
