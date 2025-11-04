# Copyright (c) Microsoft. All rights reserved.

"""Conversation storage abstraction for OpenAI Conversations API.

This module provides a clean abstraction layer for managing conversations
while wrapping AgentFramework's AgentThread underneath.
"""

import time
import uuid
from abc import ABC, abstractmethod
from typing import Any, Literal, cast

from agent_framework import AgentThread, ChatMessage
from agent_framework._workflows._checkpoint import WorkflowCheckpoint
from openai.types.conversations import Conversation, ConversationDeletedResource
from openai.types.conversations.conversation_item import ConversationItem
from openai.types.conversations.message import Message
from openai.types.conversations.text_content import TextContent
from openai.types.responses import (
    ResponseFunctionToolCallItem,
    ResponseFunctionToolCallOutputItem,
    ResponseInputFile,
    ResponseInputImage,
)

# Type alias for OpenAI Message role literals
MessageRole = Literal["unknown", "user", "assistant", "system", "critic", "discriminator", "developer", "tool"]

# Checkpoint item type constants
CONVERSATION_ITEM_TYPE_CHECKPOINT = "checkpoint"
CONVERSATION_TYPE_CHECKPOINT_CONTAINER = "checkpoint_container"


class ConversationStore(ABC):
    """Abstract base class for conversation storage.

    Provides OpenAI Conversations API interface while managing
    AgentThread instances underneath.
    """

    @abstractmethod
    def create_conversation(
        self, metadata: dict[str, str] | None = None, conversation_id: str | None = None
    ) -> Conversation:
        """Create a new conversation (wraps AgentThread creation).

        Args:
            metadata: Optional metadata dict (e.g., {"agent_id": "weather_agent"})
            conversation_id: Optional conversation ID (if None, generates one)

        Returns:
            Conversation object with generated or provided ID
        """
        pass

    @abstractmethod
    def get_conversation(self, conversation_id: str) -> Conversation | None:
        """Retrieve conversation metadata.

        Args:
            conversation_id: Conversation ID

        Returns:
            Conversation object or None if not found
        """
        pass

    @abstractmethod
    def update_conversation(self, conversation_id: str, metadata: dict[str, str]) -> Conversation:
        """Update conversation metadata.

        Args:
            conversation_id: Conversation ID
            metadata: New metadata dict

        Returns:
            Updated Conversation object

        Raises:
            ValueError: If conversation not found
        """
        pass

    @abstractmethod
    def delete_conversation(self, conversation_id: str) -> ConversationDeletedResource:
        """Delete conversation (including AgentThread).

        Args:
            conversation_id: Conversation ID

        Returns:
            ConversationDeletedResource object

        Raises:
            ValueError: If conversation not found
        """
        pass

    @abstractmethod
    async def add_items(self, conversation_id: str, items: list[dict[str, Any]]) -> list[ConversationItem]:
        """Add items to conversation (syncs to AgentThread.message_store).

        Args:
            conversation_id: Conversation ID
            items: List of conversation items to add

        Returns:
            List of added ConversationItem objects

        Raises:
            ValueError: If conversation not found
        """
        pass

    @abstractmethod
    async def add_checkpoint_item(self, conversation_id: str, checkpoint: WorkflowCheckpoint) -> ConversationItem:
        """Add checkpoint as conversation item.

        Args:
            conversation_id: Checkpoint container conversation ID
            checkpoint: WorkflowCheckpoint to store

        Returns:
            Created ConversationItem

        Raises:
            ValueError: If conversation not found
        """
        pass

    @abstractmethod
    async def list_items(
        self, conversation_id: str, limit: int = 100, after: str | None = None, order: str = "asc"
    ) -> tuple[list[ConversationItem], bool]:
        """List conversation items from AgentThread.message_store.

        Args:
            conversation_id: Conversation ID
            limit: Maximum number of items to return
            after: Cursor for pagination (item_id)
            order: Sort order ("asc" or "desc")

        Returns:
            Tuple of (items list, has_more boolean)

        Raises:
            ValueError: If conversation not found
        """
        pass

    @abstractmethod
    def get_item(self, conversation_id: str, item_id: str) -> ConversationItem | None:
        """Get specific conversation item.

        Args:
            conversation_id: Conversation ID
            item_id: Item ID

        Returns:
            ConversationItem or None if not found
        """
        pass

    @abstractmethod
    def get_thread(self, conversation_id: str) -> AgentThread | None:
        """Get underlying AgentThread for execution (internal use).

        This is the critical method that allows the executor to get the
        AgentThread for running agents with conversation context.

        Args:
            conversation_id: Conversation ID

        Returns:
            AgentThread object or None if not found
        """
        pass

    @abstractmethod
    def list_conversations_by_metadata(self, metadata_filter: dict[str, str]) -> list[Conversation]:
        """Filter conversations by metadata (e.g., agent_id).

        Args:
            metadata_filter: Metadata key-value pairs to match

        Returns:
            List of matching Conversation objects
        """
        pass


class InMemoryConversationStore(ConversationStore):
    """In-memory conversation storage wrapping AgentThread.

    This implementation stores conversations in memory with their
    underlying AgentThread instances for execution.
    """

    def __init__(self) -> None:
        """Initialize in-memory conversation storage.

        Storage structure maps conversation IDs to conversation data including
        the underlying AgentThread, metadata, and cached ConversationItems.
        """
        self._conversations: dict[str, dict[str, Any]] = {}

        # Item index for O(1) lookup: {conversation_id: {item_id: ConversationItem}}
        self._item_index: dict[str, dict[str, ConversationItem]] = {}

    def create_conversation(
        self, metadata: dict[str, str] | None = None, conversation_id: str | None = None
    ) -> Conversation:
        """Create a new conversation with underlying AgentThread."""
        conv_id = conversation_id or f"conv_{uuid.uuid4().hex}"
        created_at = int(time.time())

        # Create AgentThread with default ChatMessageStore
        thread = AgentThread()

        self._conversations[conv_id] = {
            "id": conv_id,
            "thread": thread,
            "metadata": metadata or {},
            "created_at": created_at,
            "items": [],
        }

        # Initialize item index for this conversation
        self._item_index[conv_id] = {}

        return Conversation(id=conv_id, object="conversation", created_at=created_at, metadata=metadata)

    def get_conversation(self, conversation_id: str) -> Conversation | None:
        """Retrieve conversation metadata."""
        conv_data = self._conversations.get(conversation_id)
        if not conv_data:
            return None

        return Conversation(
            id=conv_data["id"],
            object="conversation",
            created_at=conv_data["created_at"],
            metadata=conv_data.get("metadata"),
        )

    def update_conversation(self, conversation_id: str, metadata: dict[str, str]) -> Conversation:
        """Update conversation metadata."""
        conv_data = self._conversations.get(conversation_id)
        if not conv_data:
            raise ValueError(f"Conversation {conversation_id} not found")

        conv_data["metadata"] = metadata

        return Conversation(
            id=conv_data["id"],
            object="conversation",
            created_at=conv_data["created_at"],
            metadata=metadata,
        )

    def delete_conversation(self, conversation_id: str) -> ConversationDeletedResource:
        """Delete conversation and its AgentThread."""
        if conversation_id not in self._conversations:
            raise ValueError(f"Conversation {conversation_id} not found")

        del self._conversations[conversation_id]
        # Cleanup item index
        self._item_index.pop(conversation_id, None)

        return ConversationDeletedResource(id=conversation_id, object="conversation.deleted", deleted=True)

    async def add_items(self, conversation_id: str, items: list[dict[str, Any]]) -> list[ConversationItem]:
        """Add items to conversation and sync to AgentThread."""
        conv_data = self._conversations.get(conversation_id)
        if not conv_data:
            raise ValueError(f"Conversation {conversation_id} not found")

        thread: AgentThread = conv_data["thread"]

        # Convert items to ChatMessages and add to thread
        chat_messages = []
        for item in items:
            # Simple conversion - assume text content for now
            role = item.get("role", "user")
            content = item.get("content", [])
            text = content[0].get("text", "") if content else ""

            chat_msg = ChatMessage(role=role, contents=[{"type": "text", "text": text}])
            chat_messages.append(chat_msg)

        # Add messages to AgentThread
        await thread.on_new_messages(chat_messages)

        # Create Message objects (ConversationItem is a Union - use concrete Message type)
        conv_items: list[ConversationItem] = []
        for msg in chat_messages:
            item_id = f"item_{uuid.uuid4().hex}"

            # Extract role - handle both string and enum
            role_str = msg.role.value if hasattr(msg.role, "value") else str(msg.role)
            role = cast(MessageRole, role_str)  # Safe: Agent Framework roles match OpenAI roles

            # Convert ChatMessage contents to OpenAI TextContent format
            message_content = []
            for content_item in msg.contents:
                if hasattr(content_item, "type") and content_item.type == "text":
                    # Extract text from TextContent object
                    text_value = getattr(content_item, "text", "")
                    message_content.append(TextContent(type="text", text=text_value))

            # Create Message object (concrete type from ConversationItem union)
            message = Message(
                id=item_id,
                type="message",  # Required discriminator for union
                role=role,
                content=message_content,
                status="completed",  # Required field
            )
            conv_items.append(message)

        # Cache items
        conv_data["items"].extend(conv_items)

        # Update item index for O(1) lookup
        if conversation_id not in self._item_index:
            self._item_index[conversation_id] = {}

        for conv_item in conv_items:
            if conv_item.id:  # Guard against None
                self._item_index[conversation_id][conv_item.id] = conv_item

        return conv_items

    async def add_checkpoint_item(self, conversation_id: str, checkpoint: WorkflowCheckpoint) -> ConversationItem:
        """Add checkpoint as conversation item."""
        conv_data = self._conversations.get(conversation_id)
        if not conv_data:
            raise ValueError(f"Conversation {conversation_id} not found")

        from datetime import datetime

        # Convert checkpoint to conversation item dict
        item_dict = {
            "id": checkpoint.checkpoint_id,
            "type": CONVERSATION_ITEM_TYPE_CHECKPOINT,
            "object": "conversation.item.checkpoint",
            "created_at": int(datetime.fromisoformat(checkpoint.timestamp).timestamp()),
            "checkpoint_data": checkpoint.to_dict(),
        }

        # Store as-is (checkpoints don't go to AgentThread)
        conv_data["items"].append(item_dict)

        # Update item index for O(1) lookup
        if conversation_id not in self._item_index:
            self._item_index[conversation_id] = {}

        # Cast to ConversationItem for storage in index
        conv_item = cast(ConversationItem, item_dict)
        self._item_index[conversation_id][checkpoint.checkpoint_id] = conv_item

        return conv_item

    async def list_items(
        self, conversation_id: str, limit: int = 100, after: str | None = None, order: str = "asc"
    ) -> tuple[list[ConversationItem], bool]:
        """List conversation items from AgentThread message store.

        Converts AgentFramework ChatMessages to proper OpenAI ConversationItem types:
        - Messages with text/images/files → Message
        - Function calls → ResponseFunctionToolCallItem
        - Function results → ResponseFunctionToolCallOutputItem
        """
        conv_data = self._conversations.get(conversation_id)
        if not conv_data:
            raise ValueError(f"Conversation {conversation_id} not found")

        thread: AgentThread = conv_data["thread"]

        # Get messages from thread's message store
        items: list[ConversationItem] = []
        if thread.message_store:
            af_messages = await thread.message_store.list_messages()

            # Convert each AgentFramework ChatMessage to appropriate ConversationItem type(s)
            for i, msg in enumerate(af_messages):
                item_id = f"item_{i}"
                role_str = msg.role.value if hasattr(msg.role, "value") else str(msg.role)
                role = cast(MessageRole, role_str)  # Safe: Agent Framework roles match OpenAI roles

                # Process each content item in the message
                # A single ChatMessage may produce multiple ConversationItems
                # (e.g., a message with both text and a function call)
                message_contents: list[TextContent | ResponseInputImage | ResponseInputFile] = []
                function_calls = []
                function_results = []

                for content in msg.contents:
                    content_type = getattr(content, "type", None)

                    if content_type == "text":
                        # Text content for Message
                        text_value = getattr(content, "text", "")
                        message_contents.append(TextContent(type="text", text=text_value))

                    elif content_type == "data":
                        # Data content (images, files, PDFs)
                        uri = getattr(content, "uri", "")
                        media_type = getattr(content, "media_type", None)

                        if media_type and media_type.startswith("image/"):
                            # Convert to ResponseInputImage
                            message_contents.append(
                                ResponseInputImage(type="input_image", image_url=uri, detail="auto")
                            )
                        else:
                            # Convert to ResponseInputFile
                            # Extract filename from URI if possible
                            filename = None
                            if media_type == "application/pdf":
                                filename = "document.pdf"

                            message_contents.append(
                                ResponseInputFile(type="input_file", file_url=uri, filename=filename)
                            )

                    elif content_type == "function_call":
                        # Function call - create separate ConversationItem
                        call_id = getattr(content, "call_id", None)
                        name = getattr(content, "name", "")
                        arguments = getattr(content, "arguments", "")

                        if call_id and name:
                            function_calls.append(
                                ResponseFunctionToolCallItem(
                                    id=f"{item_id}_call_{call_id}",
                                    call_id=call_id,
                                    name=name,
                                    arguments=arguments,
                                    type="function_call",
                                    status="completed",
                                )
                            )

                    elif content_type == "function_result":
                        # Function result - create separate ConversationItem
                        call_id = getattr(content, "call_id", None)
                        # Output is stored in additional_properties
                        output = ""
                        if hasattr(content, "additional_properties"):
                            output = content.additional_properties.get("output", "")

                        if call_id:
                            function_results.append(
                                ResponseFunctionToolCallOutputItem(
                                    id=f"{item_id}_result_{call_id}",
                                    call_id=call_id,
                                    output=output,
                                    type="function_call_output",
                                    status="completed",
                                )
                            )

                # Create ConversationItems based on what we found
                # If message has text/images/files, create a Message item
                if message_contents:
                    message = Message(
                        id=item_id,
                        type="message",
                        role=role,  # type: ignore
                        content=message_contents,  # type: ignore
                        status="completed",
                    )
                    items.append(message)

                # Add function call items
                items.extend(function_calls)

                # Add function result items
                items.extend(function_results)

        # Include checkpoint items stored directly in conv_data["items"]
        for stored_item in conv_data.get("items", []):
            # Check if it's a checkpoint item (not already converted Message)
            if isinstance(stored_item, dict) and stored_item.get("type") == CONVERSATION_ITEM_TYPE_CHECKPOINT:
                items.append(cast(ConversationItem, stored_item))

        # Apply pagination
        if order == "desc":
            items = items[::-1]

        start_idx = 0
        if after:
            # Find the index after the cursor
            for i, item in enumerate(items):
                if item.id == after:
                    start_idx = i + 1
                    break

        paginated_items = items[start_idx : start_idx + limit]
        has_more = len(items) > start_idx + limit

        return paginated_items, has_more

    def get_item(self, conversation_id: str, item_id: str) -> ConversationItem | None:
        """Get specific conversation item - O(1) lookup via index."""
        # Use index for O(1) lookup instead of linear search
        conv_items = self._item_index.get(conversation_id)
        if not conv_items:
            return None

        return conv_items.get(item_id)

    def get_thread(self, conversation_id: str) -> AgentThread | None:
        """Get AgentThread for execution - CRITICAL for agent.run_stream()."""
        conv_data = self._conversations.get(conversation_id)
        return conv_data["thread"] if conv_data else None

    def list_conversations_by_metadata(self, metadata_filter: dict[str, str]) -> list[Conversation]:
        """Filter conversations by metadata (e.g., agent_id)."""
        results = []
        for conv_data in self._conversations.values():
            conv_meta = conv_data.get("metadata", {})
            # Check if all filter items match
            if all(conv_meta.get(k) == v for k, v in metadata_filter.items()):
                results.append(
                    Conversation(
                        id=conv_data["id"],
                        object="conversation",
                        created_at=conv_data["created_at"],
                        metadata=conv_meta,
                    )
                )
        return results


class CheckpointConversationManager:
    """Manages checkpoints as conversation items.

    This provides the glue between workflow checkpointing and conversations API.
    Checkpoints are stored as special conversation items in a dedicated
    "checkpoint container" conversation per entity.
    """

    def __init__(self, conversation_store: ConversationStore):
        self.conversation_store = conversation_store
        self._checkpoint_conv_cache: dict[str, str] = {}  # entity_id → conv_id

    async def get_or_create_checkpoint_conversation(self, entity_id: str) -> str:
        """Get or create checkpoint container conversation for entity.

        Args:
            entity_id: Entity ID (e.g., "spam_workflow")

        Returns:
            Conversation ID for checkpoint storage
        """
        import logging

        logger = logging.getLogger(__name__)

        # Check cache
        if entity_id in self._checkpoint_conv_cache:
            return self._checkpoint_conv_cache[entity_id]

        # Check if exists
        conversations = self.conversation_store.list_conversations_by_metadata({
            "entity_id": entity_id,
            "type": CONVERSATION_TYPE_CHECKPOINT_CONTAINER,
        })

        if conversations:
            conv_id = conversations[0].id
            self._checkpoint_conv_cache[entity_id] = conv_id
            return conv_id

        # Create new checkpoint conversation
        conv = self.conversation_store.create_conversation(
            metadata={
                "entity_id": entity_id,
                "type": CONVERSATION_TYPE_CHECKPOINT_CONTAINER,
                "name": f"Checkpoints for {entity_id}",
            },
            conversation_id=f"checkpoints_{entity_id}",
        )

        self._checkpoint_conv_cache[entity_id] = conv.id
        logger.info(f"Created checkpoint conversation for entity {entity_id}")
        return conv.id

    async def save_checkpoint(self, entity_id: str, checkpoint: WorkflowCheckpoint) -> str:
        """Save checkpoint as conversation item.

        Args:
            entity_id: Entity ID
            checkpoint: Checkpoint to save

        Returns:
            Checkpoint ID
        """
        conv_id = await self.get_or_create_checkpoint_conversation(entity_id)
        await self.conversation_store.add_checkpoint_item(conv_id, checkpoint)
        return checkpoint.checkpoint_id

    async def load_checkpoint(self, entity_id: str, checkpoint_id: str) -> WorkflowCheckpoint | None:
        """Load checkpoint by ID.

        Args:
            entity_id: Entity ID
            checkpoint_id: Checkpoint ID

        Returns:
            WorkflowCheckpoint or None if not found
        """
        conv_id = await self.get_or_create_checkpoint_conversation(entity_id)

        # Get specific item
        item = self.conversation_store.get_item(conv_id, checkpoint_id)
        if not item:
            return None

        # Type narrowing: verify it's a checkpoint item dict
        if isinstance(item, dict) and item.get("type") == CONVERSATION_ITEM_TYPE_CHECKPOINT:
            checkpoint_data = item.get("checkpoint_data")
            if isinstance(checkpoint_data, dict):
                return WorkflowCheckpoint.from_dict(checkpoint_data)

        return None

    async def list_checkpoints(self, entity_id: str, workflow_id: str | None = None) -> list[WorkflowCheckpoint]:
        """List checkpoints for entity.

        Args:
            entity_id: Entity ID
            workflow_id: Optional workflow ID filter

        Returns:
            List of WorkflowCheckpoint objects
        """
        conv_id = await self.get_or_create_checkpoint_conversation(entity_id)

        # List all items
        items, _ = await self.conversation_store.list_items(conv_id, limit=1000)

        # Filter checkpoint items with proper type narrowing
        checkpoints: list[WorkflowCheckpoint] = []
        for item in items:
            if isinstance(item, dict) and item.get("type") == CONVERSATION_ITEM_TYPE_CHECKPOINT:
                checkpoint_data = item.get("checkpoint_data")
                if isinstance(checkpoint_data, dict):
                    checkpoints.append(WorkflowCheckpoint.from_dict(checkpoint_data))

        # Filter by workflow_id if provided
        if workflow_id:
            checkpoints = [cp for cp in checkpoints if cp.workflow_id == workflow_id]

        return checkpoints

    def get_checkpoint_storage(self, entity_id: str) -> "ConversationItemCheckpointStorage":
        """Get CheckpointStorage adapter for entity.

        Args:
            entity_id: Entity ID

        Returns:
            CheckpointStorage implementation backed by conversation items
        """
        return ConversationItemCheckpointStorage(self, entity_id)


class ConversationItemCheckpointStorage:
    """CheckpointStorage implementation using conversation items.

    This adapter makes conversation items look like a CheckpointStorage
    to the workflow runtime, allowing transparent checkpoint save/load.
    """

    def __init__(self, manager: CheckpointConversationManager, entity_id: str):
        self.manager = manager
        self.entity_id = entity_id

    async def save_checkpoint(self, checkpoint: WorkflowCheckpoint) -> str:
        """Save checkpoint (implements CheckpointStorage protocol)."""
        return await self.manager.save_checkpoint(self.entity_id, checkpoint)

    async def load_checkpoint(self, checkpoint_id: str) -> WorkflowCheckpoint | None:
        """Load checkpoint (implements CheckpointStorage protocol)."""
        return await self.manager.load_checkpoint(self.entity_id, checkpoint_id)

    async def list_checkpoint_ids(self, workflow_id: str | None = None) -> list[str]:
        """List checkpoint IDs (implements CheckpointStorage protocol)."""
        checkpoints = await self.manager.list_checkpoints(self.entity_id, workflow_id)
        return [cp.checkpoint_id for cp in checkpoints]

    async def list_checkpoints(self, workflow_id: str | None = None) -> list[WorkflowCheckpoint]:
        """List checkpoints (implements CheckpointStorage protocol)."""
        return await self.manager.list_checkpoints(self.entity_id, workflow_id)
