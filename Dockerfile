FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
ARG DOTNET_RID
WORKDIR /src
COPY . .
RUN set -eu; \
    if [ -z "${DOTNET_RID:-}" ]; then \
      case "${TARGETARCH}" in amd64) DOTNET_RID=linux-x64 ;; arm64) DOTNET_RID=linux-arm64 ;; *) echo "Unsupported architecture: ${TARGETARCH}" >&2; exit 1 ;; esac; \
    fi; \
    dotnet publish SrumECmd/SrumECmd.csproj -c Release -f net9.0 \
      -r "${DOTNET_RID}" --self-contained true \
      -p:InvariantGlobalization=true -o /app/bin/SrumECmd

FROM build AS test
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-venv \
    && rm -rf /var/lib/apt/lists/*
RUN python3 -m venv /opt/srum-python \
    && /opt/srum-python/bin/pip install --no-cache-dir -r SrumData/python/requirements.txt
ENV SRUM_PYTHON=/opt/srum-python/bin/python
RUN /opt/srum-python/bin/python -m unittest discover -s SrumTest/python -v \
    && dotnet test SrumTest/SrumTest.csproj -c Release \
    && dotnet build SrumECmd/SrumECmd.csproj -c Release

# Binary-only export; running outside the image still needs Python + requirements.
FROM scratch AS export
COPY --from=build /app/bin/SrumECmd /

# Last stage is the runnable image, including the non-Windows ESE reader.
FROM ubuntu:24.04 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python3-venv ca-certificates \
       libstdc++6 libssl3t64 zlib1g \
    && rm -rf /var/lib/apt/lists/*
COPY SrumData/python/requirements.txt /opt/srum-python-requirements.txt
RUN python3 -m venv /opt/srum-python \
    && /opt/srum-python/bin/pip install --no-cache-dir -r /opt/srum-python-requirements.txt
WORKDIR /app
COPY --from=build /app/bin/SrumECmd ./bin/SrumECmd/
ENV SRUM_PYTHON=/opt/srum-python/bin/python \
    PYTHONDONTWRITEBYTECODE=1 \
    PATH="/app/bin/SrumECmd:${PATH}"
ENV DOTNET_EnableDiagnostics=0
USER 65532:65532
ENTRYPOINT ["SrumECmd"]
