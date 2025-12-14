# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Copy csproj and restore dependencies
COPY src/*.csproj ./src/
COPY tests/*.csproj ./tests/
COPY dependency-calculator.sln ./
RUN dotnet restore

# Copy everything else and build
COPY src/ ./src/
RUN dotnet publish ./src/src.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create a non-root user to run the application
RUN groupadd -r appgroup && useradd -r -g appgroup -s /sbin/nologin appuser

# Install SQLite (if needed for any CLI operations)
RUN apt-get update && apt-get install -y sqlite3 && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Create directory for database files
RUN mkdir -p /app/data

# Set ownership of the application directory to the new user
RUN chown -R appuser:appgroup /app

# Switch to the non-root user
USER appuser

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV API_BASE_URL=http://dependency-calculator:8080

# Expose port
EXPOSE 8080

# Run the application
ENTRYPOINT ["dotnet", "dependency-calculator.dll"]
