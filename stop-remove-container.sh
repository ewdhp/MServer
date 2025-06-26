#!/bin/sh

CONTAINER_NAME="mserver-dev-container"

# Stop the container if running
docker stop "$CONTAINER_NAME"

# Remove the container
docker rm "$CONTAINER_NAME"
