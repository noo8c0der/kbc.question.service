FROM mcr.microsoft.com/dotnet/sdk:6.0-focal-arm64v8 AS build-env
WORKDIR /app

#copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

#Copy everything else and build
COPY . ./
RUN dotnet publish -c release -o out

#Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0-focal-arm64v8
WORKDIR /app
COPY --from=build-env /app/out .

ENTRYPOINT ["dotnet", "kbc.question.service.dll"]