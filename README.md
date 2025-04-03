Dependencies: Ensure docker, gcloud, and buildx are installed on the machine running the script.

Permissions: The script assumes your Google Cloud credentials are already set up (e.g., via gcloud auth login).

Environment File: Verify env-vars.yaml exists in the same directory, or adjust the path in the script.

To deploy to Google Cloud Run
./deploy.sh

To build, run
./build-api.sh