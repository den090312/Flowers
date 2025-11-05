FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Notification/Notification.csproj", "Notification/"]
RUN dotnet restore "Notification/Notification.csproj"
COPY . .
RUN dotnet publish "Notification/Notification.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Notification.dll"]