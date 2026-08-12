# ADR 0001: Modular monolith and PostgreSQL durable work

Status: accepted

The system uses a modular monolith with independently scalable API and worker hosts. PostgreSQL stores domain state, inbox work, delivery work, leases, attempts, and dead letters.

This avoids a distributed transaction between the application database and a broker. A broker may wake workers but cannot be the sole record of pending work. Horizontal workers claim bounded batches with expiring leases.
