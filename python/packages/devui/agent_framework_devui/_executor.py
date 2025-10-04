# Copyright (c) Microsoft. All rights reserved.

"""Agent Framework executor implementation."""

import json
import logging
import os
import uuid
from collections.abc import AsyncGenerator
from typing import Any, get_origin

from agent_framework import AgentThread

from ._discovery import EntityDiscovery
from ._mapper import MessageMapper
from ._tracing import capture_traces
from .models import AgentFrameworkRequest, OpenAIResponse
from .models._discovery_models import EntityInfo

logger = logging.getLogger(__name__)


class EntityNotFoundError(Exception):
    """Raised when an entity is not found."""

    pass


class AgentFrameworkExecutor:
    """Executor for Agent Framework entities - agents and workflows."""

    def __init__(self, entity_discovery: EntityDiscovery, message_mapper: MessageMapper):
        """Initialize Agent Framework executor.

        Args:
            entity_discovery: Entity discovery instance
            message_mapper: Message mapper instance
        """
        self.entity_discovery = entity_discovery
        self.message_mapper = message_mapper
        self._setup_tracing_provider()
        self._setup_agent_framework_tracing()

        # Minimal thread storage - no metadata needed
        self.thread_storage: dict[str, AgentThread] = {}
        self.agent_threads: dict[str, list[str]] = {}  # agent_id -> thread_ids

    def _setup_tracing_provider(self) -> None:
        """Set up our own TracerProvider so we can add processors."""
        try:
            from opentelemetry import trace
            from opentelemetry.sdk.resources import Resource
            from opentelemetry.sdk.trace import TracerProvider

            # Only set up if no provider exists yet
            if not hasattr(trace, "_TRACER_PROVIDER") or trace._TRACER_PROVIDER is None:
                resource = Resource.create({
                    "service.name": "agent-framework-server",
                    "service.version": "1.0.0",
                })
                provider = TracerProvider(resource=resource)
                trace.set_tracer_provider(provider)
                logger.info("Set up TracerProvider for server tracing")
            else:
                logger.debug("TracerProvider already exists")

        except ImportError:
            logger.debug("OpenTelemetry not available")
        except Exception as e:
            logger.warning(f"Failed to setup TracerProvider: {e}")

    def _setup_agent_framework_tracing(self) -> None:
        """Set up Agent Framework's built-in tracing."""
        # Configure Agent Framework tracing only if ENABLE_OTEL is set
        if os.environ.get("ENABLE_OTEL"):
            try:
                from agent_framework.observability import setup_observability

                setup_observability(enable_sensitive_data=True)
                logger.info("Enabled Agent Framework observability")
            except Exception as e:
                logger.warning(f"Failed to enable Agent Framework observability: {e}")
        else:
            logger.debug("ENABLE_OTEL not set, skipping observability setup")

    # Thread Management Methods
    def create_thread(self, agent_id: str) -> str:
        """Create new thread for agent."""
        thread_id = f"thread_{uuid.uuid4().hex[:8]}"
        thread = AgentThread()

        self.thread_storage[thread_id] = thread

        if agent_id not in self.agent_threads:
            self.agent_threads[agent_id] = []
        self.agent_threads[agent_id].append(thread_id)

        return thread_id

    def get_thread(self, thread_id: str) -> AgentThread | None:
        """Get AgentThread by ID."""
        return self.thread_storage.get(thread_id)

    def list_threads_for_agent(self, agent_id: str) -> list[str]:
        """List thread IDs for agent."""
        return self.agent_threads.get(agent_id, [])

    def get_agent_for_thread(self, thread_id: str) -> str | None:
        """Find which agent owns this thread."""
        for agent_id, thread_ids in self.agent_threads.items():
            if thread_id in thread_ids:
                return agent_id
        return None

    def delete_thread(self, thread_id: str) -> bool:
        """Delete thread."""
        if thread_id not in self.thread_storage:
            return False

        for _agent_id, thread_ids in self.agent_threads.items():
            if thread_id in thread_ids:
                thread_ids.remove(thread_id)
                break

        del self.thread_storage[thread_id]
        return True

    async def get_thread_messages(self, thread_id: str) -> list[dict[str, Any]]:
        """Get messages from a thread's message store, preserving all content types for UI display."""
        thread = self.get_thread(thread_id)
        if not thread or not thread.message_store:
            return []

        try:
            # Get AgentFramework ChatMessage objects from thread
            af_messages = await thread.message_store.list_messages()

            ui_messages = []
            for i, af_msg in enumerate(af_messages):
                # Extract role value (handle enum)
                role = af_msg.role.value if hasattr(af_msg.role, "value") else str(af_msg.role)

                # Skip tool/function messages - only show user and assistant messages
                if role not in ["user", "assistant"]:
                    continue

                # Extract all user-facing content (text, images, files, etc.)
                display_contents = self._extract_display_contents(af_msg.contents)

                # Skip messages with no displayable content
                if not display_contents:
                    continue

                # Extract usage information if present
                usage_data = None
                for content in af_msg.contents:
                    content_type = getattr(content, "type", None)
                    if content_type == "usage":
                        details = getattr(content, "details", None)
                        if details:
                            usage_data = {
                                "total_tokens": getattr(details, "total_token_count", 0) or 0,
                                "prompt_tokens": getattr(details, "input_token_count", 0) or 0,
                                "completion_tokens": getattr(details, "output_token_count", 0) or 0,
                            }
                        break

                ui_message = {
                    "id": af_msg.message_id or f"restored-{i}",
                    "role": role,
                    "contents": display_contents,
                    "timestamp": __import__("datetime").datetime.now().isoformat(),
                    "author_name": af_msg.author_name,
                    "message_id": af_msg.message_id,
                }

                # Add usage data if available
                if usage_data:
                    ui_message["usage"] = usage_data

                ui_messages.append(ui_message)

            logger.info(f"Restored {len(ui_messages)} display messages for thread {thread_id}")
            return ui_messages

        except Exception as e:
            logger.error(f"Error getting thread messages: {e}")
            import traceback

            logger.error(traceback.format_exc())
            return []

    def _extract_display_contents(self, contents: list[Any]) -> list[dict[str, Any]]:
        """Extract all user-facing content (text, images, files, etc.) from message contents.

        Filters out internal mechanics like function calls/results while preserving
        all content types that should be displayed in the UI.
        """
        display_contents = []

        for content in contents:
            content_type = getattr(content, "type", None)

            # Text content
            if content_type == "text":
                text = getattr(content, "text", "")

                # Handle double-encoded JSON from user messages
                if text.startswith('{"role":'):
                    try:
                        import json

                        parsed = json.loads(text)
                        if parsed.get("contents"):
                            for sub_content in parsed["contents"]:
                                if sub_content.get("type") == "text":
                                    display_contents.append({"type": "text", "text": sub_content.get("text", "")})
                    except Exception:
                        display_contents.append({"type": "text", "text": text})
                else:
                    display_contents.append({"type": "text", "text": text})

            # Data content (images, files, PDFs, etc.)
            elif content_type == "data":
                display_contents.append({
                    "type": "data",
                    "uri": getattr(content, "uri", ""),
                    "media_type": getattr(content, "media_type", None),
                })

            # URI content (external links to images/files)
            elif content_type == "uri":
                display_contents.append({
                    "type": "uri",
                    "uri": getattr(content, "uri", ""),
                    "media_type": getattr(content, "media_type", None),
                })

            # Skip function_call, function_result, and other internal content types

        return display_contents

    async def serialize_thread(self, thread_id: str) -> dict[str, Any] | None:
        """Serialize thread state for persistence."""
        thread = self.get_thread(thread_id)
        if not thread:
            return None

        try:
            # Use AgentThread's built-in serialization
            serialized_state = await thread.serialize()

            # Add our metadata
            agent_id = self.get_agent_for_thread(thread_id)
            serialized_state["metadata"] = {"agent_id": agent_id, "thread_id": thread_id}

            return serialized_state

        except Exception as e:
            logger.error(f"Error serializing thread {thread_id}: {e}")
            return None

    async def deserialize_thread(self, thread_id: str, agent_id: str, serialized_state: dict[str, Any]) -> bool:
        """Deserialize thread state from persistence."""
        try:
            thread = await AgentThread.deserialize(serialized_state)
            # Store the restored thread
            self.thread_storage[thread_id] = thread
            if agent_id not in self.agent_threads:
                self.agent_threads[agent_id] = []
            self.agent_threads[agent_id].append(thread_id)

            return True

        except Exception as e:
            logger.error(f"Error deserializing thread {thread_id}: {e}")
            return False

    async def discover_entities(self) -> list[EntityInfo]:
        """Discover all available entities.

        Returns:
            List of discovered entities
        """
        return await self.entity_discovery.discover_entities()

    def get_entity_info(self, entity_id: str) -> EntityInfo:
        """Get entity information.

        Args:
            entity_id: Entity identifier

        Returns:
            Entity information

        Raises:
            EntityNotFoundError: If entity is not found
        """
        entity_info = self.entity_discovery.get_entity_info(entity_id)
        if entity_info is None:
            raise EntityNotFoundError(f"Entity '{entity_id}' not found")
        return entity_info

    async def execute_streaming(self, request: AgentFrameworkRequest) -> AsyncGenerator[Any, None]:
        """Execute request and stream results in OpenAI format.

        Args:
            request: Request to execute

        Yields:
            OpenAI response stream events
        """
        try:
            entity_id = request.get_entity_id()
            if not entity_id:
                logger.error("No entity_id specified in request")
                return

            # Validate entity exists
            if not self.entity_discovery.get_entity_info(entity_id):
                logger.error(f"Entity '{entity_id}' not found")
                return

            # Execute entity and convert events
            async for raw_event in self.execute_entity(entity_id, request):
                openai_events = await self.message_mapper.convert_event(raw_event, request)
                for event in openai_events:
                    yield event

        except Exception as e:
            logger.exception(f"Error in streaming execution: {e}")
            # Could yield error event here

    async def execute_sync(self, request: AgentFrameworkRequest) -> OpenAIResponse:
        """Execute request synchronously and return complete response.

        Args:
            request: Request to execute

        Returns:
            Final aggregated OpenAI response
        """
        # Collect all streaming events
        events = [event async for event in self.execute_streaming(request)]

        # Aggregate into final response
        return await self.message_mapper.aggregate_to_response(events, request)

    async def execute_entity(self, entity_id: str, request: AgentFrameworkRequest) -> AsyncGenerator[Any, None]:
        """Execute the entity and yield raw Agent Framework events plus trace events.

        Args:
            entity_id: ID of entity to execute
            request: Request to execute

        Yields:
            Raw Agent Framework events and trace events
        """
        try:
            # Get entity info and object
            entity_info = self.get_entity_info(entity_id)
            entity_obj = self.entity_discovery.get_entity_object(entity_id)

            if not entity_obj:
                raise EntityNotFoundError(f"Entity object for '{entity_id}' not found")

            logger.info(f"Executing {entity_info.type}: {entity_id}")

            # Extract session_id from request for trace context
            session_id = getattr(request.extra_body, "session_id", None) if request.extra_body else None

            # Use simplified trace capture
            with capture_traces(session_id=session_id, entity_id=entity_id) as trace_collector:
                if entity_info.type == "agent":
                    async for event in self._execute_agent(entity_obj, request, trace_collector):
                        yield event
                elif entity_info.type == "workflow":
                    async for event in self._execute_workflow(entity_obj, request, trace_collector):
                        yield event
                else:
                    raise ValueError(f"Unsupported entity type: {entity_info.type}")

                # Yield any remaining trace events after execution completes
                for trace_event in trace_collector.get_pending_events():
                    yield trace_event

        except Exception as e:
            logger.exception(f"Error executing entity {entity_id}: {e}")
            # Yield error event
            yield {"type": "error", "message": str(e), "entity_id": entity_id}

    async def _execute_agent(
        self, agent: Any, request: AgentFrameworkRequest, trace_collector: Any
    ) -> AsyncGenerator[Any, None]:
        """Execute Agent Framework agent with trace collection and optional thread support.

        Args:
            agent: Agent object to execute
            request: Request to execute
            trace_collector: Trace collector to get events from

        Yields:
            Agent update events and trace events
        """
        try:
            # Convert input to proper ChatMessage or string
            user_message = self._convert_input_to_chat_message(request.input)

            # Get thread if provided in extra_body
            thread = None
            if request.extra_body and hasattr(request.extra_body, "thread_id") and request.extra_body.thread_id:
                thread_id = request.extra_body.thread_id
                thread = self.get_thread(thread_id)
                if thread:
                    logger.debug(f"Using existing thread: {thread_id}")
                else:
                    logger.warning(f"Thread {thread_id} not found, proceeding without thread")

            if isinstance(user_message, str):
                logger.debug(f"Executing agent with text input: {user_message[:100]}...")
            else:
                logger.debug(f"Executing agent with multimodal ChatMessage: {type(user_message)}")

            # Use Agent Framework's native streaming with optional thread
            if thread:
                async for update in agent.run_stream(user_message, thread=thread):
                    for trace_event in trace_collector.get_pending_events():
                        yield trace_event

                    yield update
            else:
                async for update in agent.run_stream(user_message):
                    for trace_event in trace_collector.get_pending_events():
                        yield trace_event

                    yield update

        except Exception as e:
            logger.error(f"Error in agent execution: {e}")
            yield {"type": "error", "message": f"Agent execution error: {e!s}"}

    async def _execute_workflow(
        self, workflow: Any, request: AgentFrameworkRequest, trace_collector: Any
    ) -> AsyncGenerator[Any, None]:
        """Execute Agent Framework workflow with trace collection.

        Args:
            workflow: Workflow object to execute
            request: Request to execute
            trace_collector: Trace collector to get events from

        Yields:
            Workflow events and trace events
        """
        try:
            # Get input data - prefer structured data from extra_body
            input_data: str | list[Any] | dict[str, Any]
            if request.extra_body and hasattr(request.extra_body, "input_data") and request.extra_body.input_data:
                input_data = request.extra_body.input_data
                logger.debug(f"Using structured input_data from extra_body: {type(input_data)}")
            else:
                input_data = request.input
                logger.debug(f"Using input field as fallback: {type(input_data)}")

            # Parse input based on workflow's expected input type
            parsed_input = await self._parse_workflow_input(workflow, input_data)

            logger.debug(f"Executing workflow with parsed input type: {type(parsed_input)}")

            # Use Agent Framework workflow's native streaming
            async for event in workflow.run_stream(parsed_input):
                # Yield any pending trace events first
                for trace_event in trace_collector.get_pending_events():
                    yield trace_event

                # Then yield the workflow event
                yield event

        except Exception as e:
            logger.error(f"Error in workflow execution: {e}")
            yield {"type": "error", "message": f"Workflow execution error: {e!s}"}

    def _convert_input_to_chat_message(self, input_data: Any) -> Any:
        """Convert OpenAI Responses API input to Agent Framework ChatMessage or string.

        Args:
            input_data: OpenAI ResponseInputParam (List[ResponseInputItemParam])

        Returns:
            ChatMessage for multimodal content, or string for simple text
        """
        # Import Agent Framework types
        try:
            from agent_framework import ChatMessage, DataContent, Role, TextContent
        except ImportError:
            # Fallback to string extraction if Agent Framework not available
            return self._extract_user_message_fallback(input_data)

        # Handle simple string input (backward compatibility)
        if isinstance(input_data, str):
            return input_data

        # Handle OpenAI ResponseInputParam (List[ResponseInputItemParam])
        if isinstance(input_data, list):
            return self._convert_openai_input_to_chat_message(input_data, ChatMessage, TextContent, DataContent, Role)

        # Fallback for other formats
        return self._extract_user_message_fallback(input_data)

    def _convert_openai_input_to_chat_message(
        self, input_items: list[Any], ChatMessage: Any, TextContent: Any, DataContent: Any, Role: Any
    ) -> Any:
        """Convert OpenAI ResponseInputParam to Agent Framework ChatMessage.

        Args:
            input_items: List of OpenAI ResponseInputItemParam objects (dicts or objects)
            ChatMessage: ChatMessage class for creating chat messages
            TextContent: TextContent class for text content
            DataContent: DataContent class for data/media content
            Role: Role enum for message roles

        Returns:
            ChatMessage with converted content
        """
        contents = []

        # Process each input item
        for item in input_items:
            # Handle dict format (from JSON)
            if isinstance(item, dict):
                item_type = item.get("type")
                if item_type == "message":
                    # Extract content from OpenAI message
                    message_content = item.get("content", [])

                    # Handle both string content and list content
                    if isinstance(message_content, str):
                        contents.append(TextContent(text=message_content))
                    elif isinstance(message_content, list):
                        for content_item in message_content:
                            # Handle dict content items
                            if isinstance(content_item, dict):
                                content_type = content_item.get("type")

                                if content_type == "input_text":
                                    text = content_item.get("text", "")
                                    contents.append(TextContent(text=text))

                                elif content_type == "input_image":
                                    image_url = content_item.get("image_url", "")
                                    if image_url:
                                        # Extract media type from data URI if possible
                                        # Parse media type from data URL, fallback to image/png
                                        if image_url.startswith("data:"):
                                            try:
                                                # Extract media type from data:image/jpeg;base64,... format
                                                media_type = image_url.split(";")[0].split(":")[1]
                                            except (IndexError, AttributeError):
                                                logger.warning(
                                                    f"Failed to parse media type from data URL: {image_url[:30]}..."
                                                )
                                                media_type = "image/png"
                                        else:
                                            media_type = "image/png"
                                        contents.append(DataContent(uri=image_url, media_type=media_type))

                                elif content_type == "input_file":
                                    # Handle file input
                                    file_data = content_item.get("file_data")
                                    file_url = content_item.get("file_url")
                                    filename = content_item.get("filename", "")

                                    # Determine media type from filename
                                    media_type = "application/octet-stream"  # default
                                    if filename:
                                        if filename.lower().endswith(".pdf"):
                                            media_type = "application/pdf"
                                        elif filename.lower().endswith((".png", ".jpg", ".jpeg", ".gif")):
                                            media_type = f"image/{filename.split('.')[-1].lower()}"
                                        elif filename.lower().endswith((
                                            ".wav",
                                            ".mp3",
                                            ".m4a",
                                            ".ogg",
                                            ".flac",
                                            ".aac",
                                        )):
                                            ext = filename.split(".")[-1].lower()
                                            # Normalize extensions to match audio MIME types
                                            media_type = "audio/mp4" if ext == "m4a" else f"audio/{ext}"

                                    # Use file_data or file_url
                                    if file_data:
                                        # Assume file_data is base64, create data URI
                                        data_uri = f"data:{media_type};base64,{file_data}"
                                        contents.append(DataContent(uri=data_uri, media_type=media_type))
                                    elif file_url:
                                        contents.append(DataContent(uri=file_url, media_type=media_type))

            # Handle other OpenAI input item types as needed
            # (tool calls, function results, etc.)

        # If no contents found, create a simple text message
        if not contents:
            contents.append(TextContent(text=""))

        chat_message = ChatMessage(role=Role.USER, contents=contents)

        logger.info(f"Created ChatMessage with {len(contents)} contents:")
        for idx, content in enumerate(contents):
            content_type = content.__class__.__name__
            if hasattr(content, "media_type"):
                logger.info(f"  [{idx}] {content_type} - media_type: {content.media_type}")
            else:
                logger.info(f"  [{idx}] {content_type}")

        return chat_message

    def _extract_user_message_fallback(self, input_data: Any) -> str:
        """Fallback method to extract user message as string.

        Args:
            input_data: Input data in various formats

        Returns:
            Extracted user message string
        """
        if isinstance(input_data, str):
            return input_data
        if isinstance(input_data, dict):
            # Try common field names
            for field in ["message", "text", "input", "content", "query"]:
                if field in input_data:
                    return str(input_data[field])
            # Fallback to JSON string
            return json.dumps(input_data)
        return str(input_data)

    async def _parse_workflow_input(self, workflow: Any, raw_input: Any) -> Any:
        """Parse input based on workflow's expected input type.

        Args:
            workflow: Workflow object
            raw_input: Raw input data

        Returns:
            Parsed input appropriate for the workflow
        """
        try:
            # Handle structured input
            if isinstance(raw_input, dict):
                return self._parse_structured_workflow_input(workflow, raw_input)
            return self._parse_raw_workflow_input(workflow, str(raw_input))

        except Exception as e:
            logger.warning(f"Error parsing workflow input: {e}")
            return raw_input

    def _get_start_executor_message_types(self, workflow: Any) -> tuple[Any | None, list[Any]]:
        """Return start executor and its declared input types."""
        try:
            start_executor = workflow.get_start_executor()
        except Exception as exc:  # pragma: no cover - defensive logging path
            logger.debug(f"Unable to access workflow start executor: {exc}")
            return None, []

        if not start_executor:
            return None, []

        message_types: list[Any] = []

        try:
            input_types = getattr(start_executor, "input_types", None)
        except Exception as exc:  # pragma: no cover - defensive logging path
            logger.debug(f"Failed to read executor input_types: {exc}")
        else:
            if input_types:
                message_types = list(input_types)

        if not message_types and hasattr(start_executor, "_handlers"):
            try:
                handlers = start_executor._handlers
                if isinstance(handlers, dict):
                    message_types = list(handlers.keys())
            except Exception as exc:  # pragma: no cover - defensive logging path
                logger.debug(f"Failed to read executor handlers: {exc}")

        return start_executor, message_types

    def _select_primary_input_type(self, message_types: list[Any]) -> Any | None:
        """Choose the most user-friendly input type for workflow kick-off."""
        if not message_types:
            return None

        preferred = (str, dict)

        for candidate in preferred:
            for message_type in message_types:
                if message_type is candidate:
                    return candidate
                origin = get_origin(message_type)
                if origin is candidate:
                    return candidate

        return message_types[0]

    def _parse_structured_workflow_input(self, workflow: Any, input_data: dict[str, Any]) -> Any:
        """Parse structured input data for workflow execution.

        Args:
            workflow: Workflow object
            input_data: Structured input data

        Returns:
            Parsed input for workflow
        """
        try:
            from ._utils import parse_input_for_type

            # Get the start executor and its input type
            start_executor, message_types = self._get_start_executor_message_types(workflow)
            if not start_executor:
                logger.debug("Cannot determine input type for workflow - using raw dict")
                return input_data

            if not message_types:
                logger.debug("No message types found for start executor - using raw dict")
                return input_data

            # Get the first (primary) input type
            input_type = self._select_primary_input_type(message_types)
            if input_type is None:
                logger.debug("Could not select primary input type for workflow - using raw dict")
                return input_data

            # Use consolidated parsing logic from _utils
            return parse_input_for_type(input_data, input_type)

        except Exception as e:
            logger.warning(f"Error parsing structured workflow input: {e}")
            return input_data

    def _parse_raw_workflow_input(self, workflow: Any, raw_input: str) -> Any:
        """Parse raw input string based on workflow's expected input type.

        Args:
            workflow: Workflow object
            raw_input: Raw input string

        Returns:
            Parsed input for workflow
        """
        try:
            from ._utils import parse_input_for_type

            # Get the start executor and its input type
            start_executor, message_types = self._get_start_executor_message_types(workflow)
            if not start_executor:
                logger.debug("Cannot determine input type for workflow - using raw string")
                return raw_input

            if not message_types:
                logger.debug("No message types found for start executor - using raw string")
                return raw_input

            # Get the first (primary) input type
            input_type = self._select_primary_input_type(message_types)
            if input_type is None:
                logger.debug("Could not select primary input type for workflow - using raw string")
                return raw_input

            # Use consolidated parsing logic from _utils
            return parse_input_for_type(raw_input, input_type)

        except Exception as e:
            logger.debug(f"Error parsing workflow input: {e}")
            return raw_input
