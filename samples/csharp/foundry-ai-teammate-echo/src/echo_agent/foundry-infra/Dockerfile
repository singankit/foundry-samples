FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ARG BLUEPRINT_CLIENT_ID
ARG AUTHORITY_ENDPOINT
COPY ./publish .

EXPOSE 8088

ENV ASPNETCORE_URLS=http://+:8088

ENV Connections__ServiceConnection__Settings__ClientId=${BLUEPRINT_CLIENT_ID}
ENV Connections__ServiceConnection__Settings__AuthorityEndpoint=${AUTHORITY_ENDPOINT}

ENTRYPOINT ["dotnet", "EchoAgent.dll"]
