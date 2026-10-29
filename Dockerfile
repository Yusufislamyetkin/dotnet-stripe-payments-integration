FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore in its own layer so source changes don't re-download packages.
COPY global.json Directory.Build.props ./
COPY src/StripePayments.Domain/StripePayments.Domain.csproj src/StripePayments.Domain/
COPY src/StripePayments.Application/StripePayments.Application.csproj src/StripePayments.Application/
COPY src/StripePayments.Infrastructure/StripePayments.Infrastructure.csproj src/StripePayments.Infrastructure/
COPY src/StripePayments.Api/StripePayments.Api.csproj src/StripePayments.Api/
RUN dotnet restore src/StripePayments.Api/StripePayments.Api.csproj

COPY src/ src/
RUN dotnet publish src/StripePayments.Api/StripePayments.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "StripePayments.Api.dll"]
