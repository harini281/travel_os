FROM python:3.11-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1
WORKDIR /app

# Apply current Debian security updates and remove any system setuptools package.
RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && setuptools_packages="$(dpkg-query -W -f='${db:Status-Abbrev} ${binary:Package}\n' | awk '$1 == "ii" && tolower($2) ~ /setuptools/ {print $2}')" \
    && if [ -n "$setuptools_packages" ]; then apt-get purge --yes $setuptools_packages; fi \
    && rm -rf /var/lib/apt/lists/*

# Keep tooling packages in the runtime layer at versions with current security fixes.
RUN python -m pip install --no-cache-dir --upgrade \
    "pip" "setuptools>=78.1.1" "wheel>=0.46.2" "jaraco.context>=6.1.0" \
    && find /usr/lib /usr/local/lib /opt -type d \( -name 'setuptools-70.3.0.dist-info' -o -name 'setuptools-70.3.0.egg-info' \) -prune -exec rm -rf '{}' + \
    && python -c "import setuptools; from packaging.version import Version; assert Version(setuptools.__version__) >= Version('78.1.1'), setuptools.__version__"

COPY services/ai-api/requirements.txt ./requirements.txt
RUN pip install --no-cache-dir -r requirements.txt \
    && useradd --system --uid 10001 --create-home app
COPY services/ai-api/app ./app
USER 10001:10001
EXPOSE 8001
HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=5 CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8001/health', timeout=3)"
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8001"]
