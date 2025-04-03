#!/bin/bash

# Configuration
PROJECT_ID="budget-buddy-a6b6c"
REGION="us-central1"
REPO="family-budget-repo"
SERVICE_NAME="family-budget-api"
IMAGE="us-central1-docker.pkg.dev/${PROJECT_ID}/${REPO}/${SERVICE_NAME}:latest"
ENV_FILE="env-vars.yaml"

# Step 1: Authenticate Docker with Artifact Registry
echo "Authenticating Docker with Artifact Registry..."
gcloud auth configure-docker us-central1-docker.pkg.dev --quiet

# Step 2: Build and push the Docker image
echo "Building and pushing Docker image..."
docker buildx create --name mybuilder --use 2>/dev/null || docker buildx use mybuilder
docker buildx build \
  --platform linux/amd64 \
  --tag "${IMAGE}" \
  --push \
  .

# Step 3: Deploy to Cloud Run
echo "Deploying to Cloud Run..."
gcloud run deploy "${SERVICE_NAME}" \
  --image "${IMAGE}" \
  --platform managed \
  --region "${REGION}" \
  --allow-unauthenticated \
  --flags-file "${ENV_FILE}"

echo "Deployment complete!"