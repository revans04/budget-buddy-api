# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copy only the project file first to restore dependencies (improves caching)
COPY family-budget-api.csproj ./
RUN dotnet restore "family-budget-api.csproj"
# Copy the rest of the source code
COPY . ./
# Publish the app in Release mode
RUN dotnet publish "family-budget-api.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
# Copy the published app from the build stage
COPY --from=build /app/publish .
# Do NOT copy firebase-service-account.json into the image
# Instead, we'll set GOOGLE_APPLICATION_CREDENTIALS via environment variable

# Set a static default port; Program.cs will override this with PORT at runtime
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
# Set the environment to Production
ENV ASPNETCORE_ENVIRONMENT=Production
# Expose port 8080 (Cloud Run default)
EXPOSE 8080
# Run the app
ENTRYPOINT ["dotnet", "family-budget-api.dll"]