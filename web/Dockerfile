# Build context is the web/ directory (see docker-compose) so Docker does not upload the whole repo — avoids long
# silent "freezes" on Windows when the daemon receives the build context.
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . ./
RUN npm run build -- --configuration production

FROM nginx:alpine
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
# Missing index.html makes nginx's SPA try_files fall into an internal redirect loop → HTTP 500.
RUN test -f /usr/share/nginx/html/index.html
EXPOSE 80
