#!/usr/bin/env bash
set -euo pipefail

container_name="pubsoft-test-redis-cluster"

docker run -d --rm --network host --name "$container_name" redis:7-alpine sh -c '
  redis-server --port 7000 --cluster-enabled yes --cluster-config-file /tmp/redis-7000.conf --cluster-node-timeout 5000 --cluster-announce-ip 127.0.0.1 --cluster-announce-port 7000 --cluster-announce-bus-port 17000 --appendonly no &
  redis-server --port 7001 --cluster-enabled yes --cluster-config-file /tmp/redis-7001.conf --cluster-node-timeout 5000 --cluster-announce-ip 127.0.0.1 --cluster-announce-port 7001 --cluster-announce-bus-port 17001 --appendonly no &
  redis-server --port 7002 --cluster-enabled yes --cluster-config-file /tmp/redis-7002.conf --cluster-node-timeout 5000 --cluster-announce-ip 127.0.0.1 --cluster-announce-port 7002 --cluster-announce-bus-port 17002 --appendonly no &
  wait
'

for port in 7000 7001 7002; do
  ready=false
  for attempt in {1..50}; do
    if docker exec "$container_name" redis-cli -p "$port" ping >/dev/null 2>&1; then
      ready=true
      break
    fi
  done
  if [[ "$ready" != true ]]; then
    docker logs "$container_name"
    exit 1
  fi
done

docker exec "$container_name" redis-cli --cluster create \
  127.0.0.1:7000 127.0.0.1:7001 127.0.0.1:7002 \
  --cluster-replicas 0 --cluster-yes