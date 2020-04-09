#!/usr/bin/env bash
SCRIPT=$(readlink -f "$0")
SCRIPTPATH=$(dirname "$SCRIPT")
docker-compose -f $SCRIPTPATH/IntegrationTests/docker-compose.yaml down
docker-compose -f $SCRIPTPATH/IntegrationTests/docker-compose.yaml up -d
