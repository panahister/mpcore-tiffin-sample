-- Runs once, when the PostgreSQL container is first created. One database per service that owns relational
-- data (database per service, Chris Richardson, "Microservices Patterns", 2018). They share one server on a
-- developer's machine to save memory; a service never reads another service's database. tiffin_ordering is
-- the container's own database. Tracking keeps time series in the TimescaleDB server.
CREATE DATABASE tiffin_access;
CREATE DATABASE tiffin_keycloak;
CREATE DATABASE tiffin_media;
CREATE DATABASE tiffin_restaurants;
CREATE DATABASE tiffin_payments;
CREATE DATABASE tiffin_kitchen;
CREATE DATABASE tiffin_dispatch;
CREATE DATABASE tiffin_notifications;
