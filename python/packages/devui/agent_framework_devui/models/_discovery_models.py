# Copyright (c) Microsoft. All rights reserved.

"""Discovery API models for entity information."""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field


class EnvVarRequirement(BaseModel):
    """Environment variable requirement for an entity."""

    name: str
    description: str
    required: bool = True
    example: str | None = None


class EntityInfo(BaseModel):
    """Entity information for discovery and detailed views."""

    # Always present (core entity data)
    id: str
    type: str  # "agent", "workflow"
    name: str
    description: str | None = None
    framework: str
    tools: list[str | dict[str, Any]] | None = None
    metadata: dict[str, Any] = Field(default_factory=dict)

    # Source information
    source: str = "directory"  # "directory" or "in_memory"

    # Environment variable requirements
    required_env_vars: list[EnvVarRequirement] | None = None

    # Deployment support
    deployment_supported: bool = False  # Whether entity can be deployed
    deployment_reason: str | None = None  # Explanation of why/why not entity can be deployed

    # Agent-specific fields (optional, populated when available)
    instructions: str | None = None
    model_id: str | None = None
    chat_client_type: str | None = None
    context_providers: list[str] | None = None
    middleware: list[str] | None = None

    # Workflow-specific fields (populated only for detailed info requests)
    executors: list[str] | None = None
    workflow_dump: dict[str, Any] | None = None
    input_schema: dict[str, Any] | None = None
    input_type_name: str | None = None
    start_executor_id: str | None = None


class DiscoveryResponse(BaseModel):
    """Response model for entity discovery."""

    entities: list[EntityInfo] = Field(default_factory=list)


# ============================================================================
# Deployment Models
# ============================================================================


class DeploymentConfig(BaseModel):
    """Configuration for deploying an entity."""

    entity_id: str = Field(description="Entity ID to deploy")
    resource_group: str = Field(description="Azure resource group name")
    app_name: str = Field(description="Azure Container App name")
    region: str = Field(default="eastus", description="Azure region")
    ui_mode: str = Field(default="user", description="UI mode (user or developer)")
    ui_enabled: bool = Field(default=True, description="Whether to enable web interface")
    stream: bool = Field(default=True, description="Stream deployment events")


class DeploymentEvent(BaseModel):
    """Real-time deployment event (SSE)."""

    type: str = Field(description="Event type (e.g., deploy.validating, deploy.building)")
    message: str = Field(description="Human-readable message")
    url: str | None = Field(default=None, description="Deployment URL (on completion)")
    auth_token: str | None = Field(default=None, description="Auth token (on completion, shown once)")


class Deployment(BaseModel):
    """Deployment record."""

    id: str = Field(description="Deployment ID (UUID)")
    entity_id: str = Field(description="Entity ID that was deployed")
    resource_group: str = Field(description="Azure resource group")
    app_name: str = Field(description="Azure Container App name")
    region: str = Field(description="Azure region")
    url: str = Field(description="Deployment URL")
    status: str = Field(description="Deployment status (deploying, deployed, failed)")
    created_at: str = Field(description="ISO 8601 timestamp")
    error: str | None = Field(default=None, description="Error message if failed")
