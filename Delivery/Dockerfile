FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Delivery/Delivery.csproj", "Delivery/"]
RUN dotnet restore "Delivery/Delivery.csproj"
COPY . .
RUN dotnet publish "Delivery/Delivery.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Delivery.dll"]