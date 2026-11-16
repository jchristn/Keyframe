# Keyframe dashboard: Node build stage, nginx runtime on port 8701.
# Build from the dashboard folder:  docker build -t jchristn77/keyframe-dashboard ./dashboard

FROM node:22.21.1-alpine3.22 AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM nginx:1.28.0-alpine AS runtime
RUN apk add --no-cache curl
# Server used only for the same-origin /openapi.json fallback; the browser talks to the
# server URL entered at login for everything else.
ENV KEYFRAME_SERVER_URL=http://keyframe-server:8700
ENV NGINX_RESOLVER=127.0.0.11
RUN rm -f /etc/nginx/conf.d/default.conf
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8701
HEALTHCHECK --interval=5s --timeout=3s --retries=2 CMD curl -fsS http://127.0.0.1:8701/healthz || exit 1
