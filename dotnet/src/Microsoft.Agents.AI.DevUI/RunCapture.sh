#!/bin/bash

echo "🧪 Running .NET DevUI Message Capture Test"
echo "=========================================="
echo ""

# Kill any existing servers
pkill -f "dotnet run" 2>/dev/null || true
sleep 1

# Compile and run the capture test
echo "📦 Building and running capture test..."
dotnet run -p:TreatWarningsAsErrors=false --project . -- --port 8093 --entities-dir samples > server_capture.log 2>&1 &
SERVER_PID=$!

# Wait for server to start
sleep 5

# Check if server is running
if ! curl -s http://127.0.0.1:8093/health > /dev/null; then
    echo "❌ Failed to start server"
    kill $SERVER_PID 2>/dev/null
    exit 1
fi

echo "✅ Server is running on port 8093"

# Now run the actual capture (using the main program with special capture mode)
# Since we can't easily run CaptureMessages.cs as standalone, let's use curl to capture
echo ""
echo "📋 Discovering and testing entities..."

# Create output directory
mkdir -p Tests/captured_messages

# Get entities
ENTITIES=$(curl -s http://127.0.0.1:8093/v1/entities)
echo "$ENTITIES" | jq -r '.entities[].id' 2>/dev/null | while read entity_id; do
    echo "  Testing entity: $entity_id"

    # Non-streaming test
    curl -s -X POST http://127.0.0.1:8093/v1/responses \
        -H "Content-Type: application/json" \
        -d "{\"model\":\"$entity_id\",\"messages\":[{\"role\":\"user\",\"content\":\"Test message for capture\"}],\"extra_body\":{\"entity_id\":\"$entity_id\"}}" \
        > "Tests/captured_messages/${entity_id}_nonstreaming.json" 2>/dev/null

    # Streaming test
    curl -s -X POST http://127.0.0.1:8093/v1/responses \
        -H "Content-Type: application/json" \
        -d "{\"model\":\"$entity_id\",\"messages\":[{\"role\":\"user\",\"content\":\"Test message for capture\"}],\"extra_body\":{\"entity_id\":\"$entity_id\"},\"stream\":true}" \
        > "Tests/captured_messages/${entity_id}_streaming.txt" 2>/dev/null
done

# Save all results to single file
echo "{" > Tests/captured_messages/dotnet_entities_stream_events.json
echo "  \"timestamp\": $(date +%s)," >> Tests/captured_messages/dotnet_entities_stream_events.json
echo "  \"server_type\": \"DotNetDevUI\"," >> Tests/captured_messages/dotnet_entities_stream_events.json
echo "  \"runtime\": \".NET 9.0\"," >> Tests/captured_messages/dotnet_entities_stream_events.json
echo "  \"entities\": $ENTITIES" >> Tests/captured_messages/dotnet_entities_stream_events.json
echo "}" >> Tests/captured_messages/dotnet_entities_stream_events.json

echo ""
echo "✅ Capture complete! Results saved to Tests/captured_messages/"

# Cleanup
kill $SERVER_PID 2>/dev/null
echo "🛑 Server stopped"