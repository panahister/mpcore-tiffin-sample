#!/usr/bin/env bash
# Writes the addresses of the dependencies into each service's user secrets
# (~/.microsoft/usersecrets/<id>), outside the repository. Nothing is written into a tracked file.
# Run once, and again if you change a port in infrastructure/.env.
set -euo pipefail
source "$(dirname "$0")/lib.sh"
ensure_env

user=$(env_value POSTGRES_USER tiffin); password=$(env_value POSTGRES_PASSWORD tiffin)
postgres="Host=localhost;Port=$(env_value POSTGRES_PORT 35432);Username=$user;Password=$password"
timescale="Host=localhost;Port=$(env_value TIMESCALE_PORT 35433);Username=$user;Password=$password"
redis="localhost:$(env_value REDIS_PORT 36379),abortConnect=false"
authority="http://localhost:$(env_value KEYCLOAK_PORT 38180)/realms/tiffin"
kafka="localhost:$(env_value KAFKA_PORT 39092)"
rabbit="amqp://$(env_value RABBITMQ_USER tiffin):$(env_value RABBITMQ_PASSWORD tiffin)@localhost:$(env_value RABBITMQ_PORT 35672)"
otlp="http://localhost:$(env_value OTLP_GRPC_PORT 34317)"

secret() { dotnet user-secrets --project "$1" set "$2" "$3" >/dev/null; }

for service in "${SERVICES[@]}"; do
  project="$(project_of "$service")"
  [ -d "$project" ] || { echo "   $service: not in this checkout, skipped"; continue; }
  secret "$project" "Security:Authority" "$authority"
  for signal in Logs Metrics Traces; do secret "$project" "Observability:$signal:Endpoint" "$otlp"; done
  case "$(field "$service" 3)" in
    postgres)  secret "$project" "ConnectionStrings:PostgreSql" "$postgres;Database=$(field "$service" 4)" ;;
    timescale) secret "$project" "ConnectionStrings:PostgreSql" "$timescale;Database=$(field "$service" 4)" ;;
  esac
  case "$(field "$service" 5)" in
    kafka)    secret "$project" "Messaging:Kafka:BootstrapServers" "$kafka" ;;
    rabbitmq) secret "$project" "Messaging:RabbitMq:ConnectionString" "$rabbit" ;;
  esac
  [ "$service" = "restaurants" ] && secret "$project" "ConnectionStrings:Redis" "$redis"
  # A service that calls another one does so as itself, with a secret of its own (infrastructure/keycloak).
  case "$service" in
    access|ordering|kitchen|dispatch|notifications) secret "$project" "ServiceIdentity:ClientSecret" "lab-only-$service-secret" ;;
  esac
  # Media's keys for the store of the bytes.
  if [ "$service" = "media" ]; then
    secret "$project" "Store:AccessKey" "$(env_value MEDIA_S3_ACCESS_KEY tiffin-media)"
    secret "$project" "Store:SecretKey" "$(env_value MEDIA_S3_SECRET_KEY tiffin-media-lab)"
    secret "$project" "Store:Endpoint" "http://localhost:$(env_value MEDIA_S3_PORT 39000)"
  fi
  # Ordering speaks to both brokers: requests and answers on queues, what happened on the stream.
  [ "$service" = "ordering" ] && secret "$project" "Messaging:RabbitMq:ConnectionString" "$rabbit"
  echo "   $service: user secrets written"
done
