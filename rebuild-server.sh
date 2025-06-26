#!/bin/sh

# Usage: ./rebuild-server.sh <container_name_or_id> <project_path_inside_container> <server_process_name>
# Example: ./rebuild-server.sh my_container /home/ewd/MServer MServer

CONTAINER="$1"
PROJECT_PATH="$2"
PROCESS_NAME="$3"

if [ -z "$CONTAINER" ] || [ -z "$PROJECT_PATH" ] || [ -z "$PROCESS_NAME" ]; then
  echo "Usage: $0 <container_name_or_id> <project_path_inside_container> <server_process_name>"
  exit 1
fi

echo "Stopping $PROCESS_NAME inside $CONTAINER..."
docker exec "$CONTAINER" pkill -f "$PROCESS_NAME"

echo "Rebuilding project inside $CONTAINER..."
docker exec "$CONTAINER" sh -c "cd $PROJECT_PATH && dotnet build"

echo "Restarting $PROCESS_NAME inside $CONTAINER..."
docker exec -d "$CONTAINER" sh -c "cd $PROJECT_PATH && dotnet run --no-build &"

echo "Done."
