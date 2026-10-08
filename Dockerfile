# Container build for hosts without native .NET support (e.g. Render.com)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/DlangezwaHS.Web/DlangezwaHS.Web.csproj src/DlangezwaHS.Web/
RUN dotnet restore src/DlangezwaHS.Web/DlangezwaHS.Web.csproj
COPY src/ src/
RUN dotnet publish src/DlangezwaHS.Web/DlangezwaHS.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# fontconfig is needed by QuestPDF to render PDFs on Linux
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# Render routes traffic to $PORT (10000 by default); TLS is terminated by Render's proxy
ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=10000
EXPOSE 10000
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT} exec dotnet DlangezwaHS.Web.dll"]
