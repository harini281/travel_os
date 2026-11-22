FROM node:22-alpine AS build
WORKDIR /workspace
COPY apps/web/package.json ./package.json
RUN npm install --no-audit --no-fund
COPY apps/web/ ./
ARG VITE_PLATFORM_API_URL=
ENV VITE_PLATFORM_API_URL=$VITE_PLATFORM_API_URL
RUN npm run build
FROM nginx/nginx-unprivileged:1.31.9-alpine3.24 AS runtime
# Refresh the runtime OS security package set; the upstream tag can lag patched Alpine revisions.
USER root
RUN apk upgrade --no-cache libxml2
USER 101
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /workspace/dist/ /usr/share/nginx/html/
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/ || exit 1
