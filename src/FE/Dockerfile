FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . ./
ARG VITE_API_BASE_URL=/
ARG VITE_DEMO_MODE=false
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_DEMO_MODE=$VITE_DEMO_MODE
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/ /usr/share/nginx/html/
EXPOSE 8080