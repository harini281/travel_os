# Build Python dependencies in a disposable builder image. The final image copies
# only the cleaned /usr/local runtime tree, not the builder's distro/package layers.
FROM python:3.11.18-slim-trixie AS builder
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1
WORKDIR /build

RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*

RUN python -m pip install --no-cache-dir --upgrade \
    "pip" "setuptools>=78.1.1" "wheel>=0.46.2" "jaraco.context>=6.1.0"

COPY services/ai-api/requirements.txt ./requirements.txt
RUN python -m pip install --no-cache-dir -r requirements.txt \
    && python -m pip uninstall --yes setuptools wheel pip \
    && rm -rf /usr/local/lib/python3.11/site-packages/setuptools* \
              /usr/local/lib/python3.11/site-packages/wheel* \
              /usr/local/lib/python3.11/site-packages/pip* \
              /usr/local/bin/pip*

# Fail the builder if any packaging-tool metadata survived cleanup.
RUN python -c "import importlib.metadata as m; names={d.metadata['Name'].lower() for d in m.distributions()}; assert not names.intersection({'setuptools','wheel','pip'}), names"

FROM debian:trixie-slim AS runtime
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PATH=/usr/local/bin:$PATH
WORKDIR /app

# Install only shared libraries required by the official CPython runtime.
RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && apt-get install --yes --no-install-recommends \
        ca-certificates libbz2-1.0 libcrypt1 libexpat1 libffi8 libgdbm6 \
        libgdbm-compat4 liblzma5 libncursesw6 libreadline8 libsqlite3-0 \
        libssl3 libtinfo6 libuuid1 zlib1g \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /usr/local/ /usr/local/
COPY --chown=10001:10001 services/ai-api/app ./app
USER 10001:10001
EXPOSE 8001

# Verify shared-library links and the installed application runtime without
# requiring deployment-only provider secrets during the image build.
RUN ldconfig \
    && python -c "import importlib.metadata as m; names={d.metadata['Name'].lower() for d in m.distributions()}; assert not names.intersection({'setuptools','wheel','pip'}), names; import fastapi, pydantic, uvicorn"

HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=5 CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8001/health', timeout=3)"
CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8001"]
