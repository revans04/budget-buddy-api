#!/bin/bash
docker stop family-budget-api
echo "Stop Docker image..."

docker rm family-budget-api
echo "Remove Docker image..."

docker build -t family-budget-api .
echo "Build API..."

docker run -d -p 8080:8080 \
  --name family-budget-api \
  -e GOOGLE_APPLICATION_CREDENTIALS_JSON="$(cat firebase-service-account.json)" \
  family-budget-api
echo "Running new Docker container..."
