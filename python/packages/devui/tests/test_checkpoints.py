# Copyright (c) Microsoft. All rights reserved.

"""Tests for checkpoint-as-conversation-items implementation."""

from dataclasses import dataclass

import pytest
from agent_framework import (
    Executor,
    InMemoryCheckpointStorage,
    WorkflowBuilder,
    WorkflowContext,
    handler,
    response_handler,
)

from agent_framework_devui._conversations import (
    CONVERSATION_ITEM_TYPE_CHECKPOINT,
    CONVERSATION_TYPE_CHECKPOINT_CONTAINER,
    CheckpointConversationManager,
    InMemoryConversationStore,
)


@dataclass
class WorkflowTestData:
    """Simple test data."""

    value: str


@dataclass
class WorkflowHILRequest:
    """HIL request for testing."""

    question: str


class WorkflowTestExecutor(Executor):
    """Test executor with HIL."""

    @handler
    async def process(self, data: WorkflowTestData, ctx: WorkflowContext) -> None:
        """Process data and request approval."""
        await ctx.set_executor_state({"data_value": data.value})

        # Request HIL (checkpoint created here)
        await ctx.request_info(request_data=WorkflowHILRequest(question=f"Approve {data.value}?"), response_type=str)

    @response_handler
    async def handle_response(
        self, original_request: WorkflowHILRequest, response: str, ctx: WorkflowContext[str]
    ) -> None:
        """Handle HIL response."""
        state = await ctx.get_executor_state() or {}
        value = state.get("data_value", "")
        await ctx.send_message(f"{value}_approved" if response.lower() == "yes" else f"{value}_rejected")


@pytest.fixture
def conversation_store():
    """Create in-memory conversation store."""
    return InMemoryConversationStore()


@pytest.fixture
def checkpoint_manager(conversation_store):
    """Create checkpoint manager."""
    return CheckpointConversationManager(conversation_store)


@pytest.fixture
def test_workflow():
    """Create test workflow with checkpointing."""
    executor = WorkflowTestExecutor(id="test_executor")
    checkpoint_storage = InMemoryCheckpointStorage()

    return (
        WorkflowBuilder(name="Test Workflow", description="Test checkpoint behavior")
        .set_start_executor(executor)
        .with_checkpointing(checkpoint_storage)
        .build()
    )


class TestCheckpointConversationManager:
    """Test CheckpointConversationManager functionality."""

    @pytest.mark.asyncio
    async def test_create_checkpoint_conversation(self, checkpoint_manager):
        """Test checkpoint conversation creation."""
        entity_id = "test_entity"

        # Get or create checkpoint conversation
        conv_id = await checkpoint_manager.get_or_create_checkpoint_conversation(entity_id)

        assert conv_id == f"checkpoints_{entity_id}"

        # Verify conversation exists
        conv = checkpoint_manager.conversation_store.get_conversation(conv_id)
        assert conv is not None
        assert conv.metadata["type"] == CONVERSATION_TYPE_CHECKPOINT_CONTAINER
        assert conv.metadata["entity_id"] == entity_id

    @pytest.mark.asyncio
    async def test_save_checkpoint(self, checkpoint_manager, test_workflow):
        """Test saving checkpoint as conversation item."""
        entity_id = "test_entity"

        # Create test checkpoint
        import uuid

        from agent_framework._workflows._checkpoint import WorkflowCheckpoint

        checkpoint = WorkflowCheckpoint(
            checkpoint_id=str(uuid.uuid4()), workflow_id=test_workflow.id, messages={}, shared_state={}
        )

        # Save checkpoint via manager
        checkpoint_id = await checkpoint_manager.save_checkpoint(entity_id, checkpoint)

        assert checkpoint_id == checkpoint.checkpoint_id

        # Verify checkpoint stored as conversation item
        conv_id = await checkpoint_manager.get_or_create_checkpoint_conversation(entity_id)
        items, _ = await checkpoint_manager.conversation_store.list_items(conv_id)

        # Items might be ConversationItem objects or dicts, handle both
        checkpoint_items = []
        for item in items:
            item_type = item.get("type") if isinstance(item, dict) else getattr(item, "type", None)
            if item_type == CONVERSATION_ITEM_TYPE_CHECKPOINT:
                checkpoint_items.append(item)

        assert len(checkpoint_items) == 1
        first_item = checkpoint_items[0]
        item_id = first_item.get("id") if isinstance(first_item, dict) else getattr(first_item, "id", None)
        item_data = (
            first_item.get("checkpoint_data")
            if isinstance(first_item, dict)
            else getattr(first_item, "checkpoint_data", None)
        )
        assert item_id == checkpoint_id
        assert item_data["checkpoint_id"] == checkpoint_id

    @pytest.mark.asyncio
    async def test_load_checkpoint(self, checkpoint_manager, test_workflow):
        """Test loading checkpoint from conversation items."""
        entity_id = "test_entity"

        # Create and save a checkpoint
        import uuid

        from agent_framework._workflows._checkpoint import WorkflowCheckpoint

        original_checkpoint = WorkflowCheckpoint(
            checkpoint_id=str(uuid.uuid4()),
            workflow_id=test_workflow.id,
            messages={},
            shared_state={"test_key": "test_value"},
        )

        # Save via manager
        await checkpoint_manager.save_checkpoint(entity_id, original_checkpoint)

        # Load checkpoint via manager
        loaded_checkpoint = await checkpoint_manager.load_checkpoint(entity_id, original_checkpoint.checkpoint_id)

        assert loaded_checkpoint is not None
        assert loaded_checkpoint.checkpoint_id == original_checkpoint.checkpoint_id
        assert loaded_checkpoint.workflow_id == original_checkpoint.workflow_id
        assert loaded_checkpoint.shared_state == {"test_key": "test_value"}

    @pytest.mark.asyncio
    async def test_list_checkpoints(self, checkpoint_manager, test_workflow):
        """Test listing checkpoints for entity."""
        entity_id = "test_entity"

        # Create and save multiple checkpoints
        import uuid

        from agent_framework._workflows._checkpoint import WorkflowCheckpoint

        for i in range(2):
            checkpoint = WorkflowCheckpoint(
                checkpoint_id=str(uuid.uuid4()),
                workflow_id=test_workflow.id,
                messages={},
                shared_state={"iteration": i},
            )
            await checkpoint_manager.save_checkpoint(entity_id, checkpoint)

        # List checkpoints
        checkpoints = await checkpoint_manager.list_checkpoints(entity_id, workflow_id=test_workflow.id)

        assert len(checkpoints) == 2
        assert all(cp.workflow_id == test_workflow.id for cp in checkpoints)


class TestCheckpointStorageAdapter:
    """Test ConversationItemCheckpointStorage adapter."""

    @pytest.mark.asyncio
    async def test_checkpoint_storage_protocol(self, checkpoint_manager, test_workflow):
        """Test that adapter implements CheckpointStorage protocol."""
        entity_id = "test_entity"

        # Get storage adapter
        storage = checkpoint_manager.get_checkpoint_storage(entity_id)

        # Create test checkpoint
        import uuid

        from agent_framework._workflows._checkpoint import WorkflowCheckpoint

        checkpoint = WorkflowCheckpoint(
            checkpoint_id=str(uuid.uuid4()), workflow_id=test_workflow.id, messages={}, shared_state={"test": "data"}
        )

        # Test save_checkpoint
        checkpoint_id = await storage.save_checkpoint(checkpoint)
        assert checkpoint_id == checkpoint.checkpoint_id

        # Test load_checkpoint
        loaded = await storage.load_checkpoint(checkpoint_id)
        assert loaded is not None
        assert loaded.checkpoint_id == checkpoint_id

        # Test list_checkpoint_ids
        ids = await storage.list_checkpoint_ids(workflow_id=test_workflow.id)
        assert checkpoint_id in ids

        # Test list_checkpoints
        checkpoints_list = await storage.list_checkpoints(workflow_id=test_workflow.id)
        assert len(checkpoints_list) >= 1
        assert any(cp.checkpoint_id == checkpoint_id for cp in checkpoints_list)


class TestIntegration:
    """Integration tests for checkpoint workflow execution."""

    @pytest.mark.asyncio
    async def test_manual_checkpoint_save_via_injected_storage(self, checkpoint_manager, test_workflow):
        """Test manual checkpoint save via injected storage."""
        entity_id = "test_entity"

        # Get checkpoint storage for entity
        checkpoint_storage = checkpoint_manager.get_checkpoint_storage(entity_id)

        # Inject storage into workflow (simulating what _discovery.py does)
        if hasattr(test_workflow, "_runner") and hasattr(test_workflow._runner, "context"):
            test_workflow._runner.context._checkpoint_storage = checkpoint_storage

        # Create and save a checkpoint via injected storage
        import uuid

        from agent_framework._workflows._checkpoint import WorkflowCheckpoint

        checkpoint = WorkflowCheckpoint(
            checkpoint_id=str(uuid.uuid4()), workflow_id=test_workflow.id, messages={}, shared_state={"injected": True}
        )
        await checkpoint_storage.save_checkpoint(checkpoint)

        # Verify checkpoint is accessible via manager
        manager_checkpoints = await checkpoint_manager.list_checkpoints(entity_id, workflow_id=test_workflow.id)
        assert len(manager_checkpoints) > 0
        assert manager_checkpoints[0].checkpoint_id == checkpoint.checkpoint_id

    @pytest.mark.asyncio
    async def test_checkpoint_roundtrip_via_storage(self, checkpoint_manager, test_workflow):
        """Test checkpoint save/load roundtrip via storage adapter."""
        entity_id = "test_entity"

        # Inject storage
        checkpoint_storage = checkpoint_manager.get_checkpoint_storage(entity_id)
        test_workflow._runner.context._checkpoint_storage = checkpoint_storage

        # Create checkpoint
        import uuid

        from agent_framework._workflows._checkpoint import WorkflowCheckpoint

        checkpoint = WorkflowCheckpoint(
            checkpoint_id=str(uuid.uuid4()),
            workflow_id=test_workflow.id,
            messages={},
            shared_state={"ready_to_resume": True},
        )
        checkpoint_id = await checkpoint_storage.save_checkpoint(checkpoint)

        # Verify checkpoint can be loaded for resume
        loaded = await checkpoint_storage.load_checkpoint(checkpoint_id)
        assert loaded is not None
        assert loaded.checkpoint_id == checkpoint_id
        assert loaded.shared_state == {"ready_to_resume": True}

        # Verify checkpoint is accessible via manager (for UI to list checkpoints)
        checkpoints = await checkpoint_manager.list_checkpoints(entity_id, workflow_id=test_workflow.id)
        assert len(checkpoints) > 0
        assert checkpoints[0].checkpoint_id == checkpoint_id

    @pytest.mark.asyncio
    async def test_workflow_auto_saves_checkpoints_to_injected_storage(self, checkpoint_manager, test_workflow):
        """Test that workflows automatically save checkpoints to our conversation-backed storage.

        This is the critical end-to-end test that verifies the entire checkpoint flow:
        1. Storage is injected into workflow
        2. Workflow runs and pauses at HIL point (IDLE_WITH_PENDING_REQUESTS status)
        3. Framework automatically saves checkpoint to our storage
        4. Checkpoint is accessible via manager for UI to list/resume
        """
        entity_id = "test_entity"

        # Inject our storage BEFORE running workflow
        checkpoint_storage = checkpoint_manager.get_checkpoint_storage(entity_id)
        test_workflow._runner.context._checkpoint_storage = checkpoint_storage

        # Verify no checkpoints initially
        checkpoints_before = await checkpoint_manager.list_checkpoints(entity_id, workflow_id=test_workflow.id)
        assert len(checkpoints_before) == 0

        # Run workflow until it reaches IDLE_WITH_PENDING_REQUESTS (after checkpoint is created)
        saw_request_event = False
        async for event in test_workflow.run_stream(WorkflowTestData(value="test")):
            if hasattr(event, "__class__"):
                if event.__class__.__name__ == "RequestInfoEvent":
                    saw_request_event = True
                # Wait for IDLE_WITH_PENDING_REQUESTS status (comes after checkpoint creation)
                is_status_event = event.__class__.__name__ == "WorkflowStatusEvent"
                has_pending_status = hasattr(event, "status") and "IDLE_WITH_PENDING_REQUESTS" in str(event.status)
                if is_status_event and has_pending_status:
                    break

        assert saw_request_event, "Test workflow should have emitted RequestInfoEvent"

        # Verify checkpoint was AUTOMATICALLY saved to our storage by the framework
        checkpoints_after = await checkpoint_manager.list_checkpoints(entity_id, workflow_id=test_workflow.id)
        assert len(checkpoints_after) > 0, "Workflow should have auto-saved checkpoint at HIL pause"

        # Verify checkpoint has correct workflow_id
        checkpoint = checkpoints_after[0]
        assert checkpoint.workflow_id == test_workflow.id
