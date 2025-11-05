FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Billing/Billing.csproj", "Billing/"]
RUN dotnet restore "Billing/Billing.csproj"
COPY . .
RUN dotnet publish "Billing/Billing.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Billing.dll"]