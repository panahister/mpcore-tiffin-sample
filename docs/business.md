# The business

What Tiffin does, as a person of the business would say it: who the people are, what each service
decides, and every rule with its code. A rule that is added or changed is added or changed here too.

## The people

Signed in at Keycloak, realm `tiffin`. A password is the name and `-lab`: `olivia-lab`.

| Who | City | Role | Surface | What they do |
|---|---|---|---|---|
| olivia, ethan | Seattle | `customer` | Customer app | order, follow, cancel |
| madison | Seattle | `restaurant-manager` | Operations app | keeps Seattle restaurants and accepts or refuses their orders |
| noah | Seattle | `courier` | Operations app | goes on duty, carries, reports position and completes delivery |
| ava | Seattle | `city-admin` | Access API (no Operations UI) | gives and takes the roles of the city |
| emma | Austin | `customer` | Customer app | orders, follows and cancels |
| mason | Austin | `restaurant-manager` | Operations app | keeps Austin restaurants and accepts or refuses their orders |
| logan | Austin | `city-admin` | Access API (no Operations UI) | gives and takes the roles of the city |
| grace | the platform | `platform-admin` | Access API (no Operations UI) | makes admins of cities |

The Operations UI intentionally exposes restaurant and courier work only. Role/resource creation and
assignment stay in Keycloak and the Access service and this sample intentionally ships no UI for them,
so city and platform administrators must not be presented as Operations UI accounts.
`scripts/verify-demo-users.py` performs a non-mutating login, claim
and protected-read check for every human demo identity without printing a credential or token.

**The city is the tenant.** Somebody belongs to the city whose group they are in (`/cities/seattle`), the
identity provider writes it into the token as `tenant_id`, and from there it is in every message, every
row and every record of the audit trail. What belongs to one city does not exist for another: an order,
a restaurant, a file or a person of Seattle is answered to somebody of Austin with "not found", never
with "forbidden", because "forbidden" says that it exists.

## The life of an order

```text
Placed ──▶ Paid ──▶ Accepted ──▶ OutForDelivery ──▶ Delivered
   │         │          │
   └─────────┴──────────┴──▶ Cancelled      payment-declined · restaurant-refused · no-courier
                                            restaurant-did-not-answer · step-given-up
                                            cancelled-by-customer (until Accepted)
```

**A restaurant has ten minutes to answer a paid order.** When it has neither accepted nor refused by then,
the order is cancelled (`restaurant-did-not-answer`), the Kitchen is told to stop, the money goes back and
the customer is told why. An answer that comes later is refused under rule K1. The ten minutes are
`Ordering:RestaurantAnswerDeadline`.

A customer is told "accepted" (HTTP 202) the moment the order has an identity. Whether the card has the
money, whether the restaurant will cook and whether a courier is free is answered afterwards, by the
service that knows.

## The rules

Every rule is a class named after what it says, checked by the aggregate before it changes, and reported
to the caller under its own code and in the caller's language. The pattern is Kamil Grzybek's (*Modular
Monolith with DDD*); an aggregate that checks before it changes is never invalid, which Vladimir Khorikov
calls the *always-valid domain model*.

| Rule | It says | Code | Where |
|---|---|---|---|
| R1 | A price is more than nothing | `PRICE_NOT_POSITIVE` | Restaurants |
| R2 | A restaurant opens with something to sell | `MENU_EMPTY` | Restaurants |
| R3 | A closed restaurant takes no order, so it quotes none | `RESTAURANT_CLOSED` | Restaurants |
| R4 | What is ordered is on the menu, and not sold out | `ITEM_NOT_ON_SALE` | Restaurants |
| R5 | A restaurant sells in a currency the platform settles | `CURRENCY_UNKNOWN` | Restaurants |
| O1 | An order has something in it | `ORDER_EMPTY` | Ordering |
| O2 | An order is paid once, and only while it waits for its charge | `ORDER_NOT_AWAITING_PAYMENT` | Ordering |
| O3 | A restaurant accepts an order that was paid | `ORDER_NOT_AWAITING_RESTAURANT` | Ordering |
| O4 | A courier takes an order the restaurant accepted | `ORDER_NOT_AWAITING_COURIER` | Ordering |
| O5 | What is delivered was on its way | `ORDER_NOT_ON_ITS_WAY` | Ordering |
| O6 | An order that has left the restaurant, or has ended, is not cancelled | `ORDER_CANNOT_BE_CANCELLED` | Ordering |
| O7 | A customer cancels until the restaurant has started to cook | `ORDER_ALREADY_COOKING` | Ordering |
| P1 | An amount is more than nothing | `AMOUNT_NOT_POSITIVE` | Payments |
| P2 | A card is charged once for an order, or not at all | `PAYMENT_NOT_PENDING` | Payments |
| P3 | What was not charged is not given back | `PAYMENT_NOT_CHARGED` | Payments |
| K1 | A restaurant decides an order once | `TICKET_NOT_PENDING` | Kitchen |
| K2 | A restaurant promises between five minutes and three hours | `PROMISE_OUT_OF_RANGE` | Kitchen |
| K3 | There is something to cook | `TICKET_EMPTY` | Kitchen |
| D1 | A courier carries one order at a time | `COURIER_NOT_FREE` | Dispatch |
| D2 | A courier goes home after handing over what they carry | `COURIER_IS_CARRYING` | Dispatch |
| D3 | A delivery is completed by the courier who carries it | `NOT_YOUR_DELIVERY` | Dispatch |
| D4 | A delivery is completed once | `DELIVERY_ALREADY_COMPLETED` | Dispatch |
| T1 | Where a delivery is, is said by the courier who carries it | `NOT_YOUR_DELIVERY` | Tracking |
| T2 | A delivery that has arrived is not reported any more | `DELIVERY_HAS_ARRIVED` | Tracking |
| T3 | A position is on Earth | `POSITION_NOT_ON_EARTH` | Tracking |
| A1 | A role is one of the platform's | `ROLE_UNKNOWN` | Access |
| A2 | A city's admin hands out the roles of a city; only the platform's admin makes a city's admin | `ROLE_NOT_YOURS_TO_GIVE` | Access |
| A3 | Nobody changes their own roles | `OWN_ROLES` | Access |
| M1 | A file is for something the platform has files for | `PURPOSE_UNKNOWN` | Media |
| M2 | A file is of a type its purpose allows | `TYPE_NOT_ALLOWED` | Media |
| M3 | A file is not empty, and not larger than its purpose allows | `SIZE_NOT_ALLOWED` | Media |
| M4 | A file is confirmed once | `FILE_NOT_PENDING` | Media |
| M5 | What arrived in the store is what was announced | `NOT_WHAT_WAS_ANNOUNCED` | Media |

A rule's code is reported under the error domain of its service: `tiffin.ordering`, `tiffin.payments` and
so on. Notifications has no rule: it decides nothing.

## What is not a rule, and is refused all the same

An outcome that is expected is a failure a handler returns, not a rule that breaks. The ones a caller
meets most:

| Code | When | HTTP |
|---|---|---|
| `ORDER_TOTAL_CHANGED` | the order costs something else than the customer saw; the answer names the total that is true | 422 |
| `RESTAURANT_NOT_FOUND`, `ORDER_NOT_FOUND`, `FILE_NOT_FOUND`, `PERSON_NOT_FOUND` | it does not exist, or it belongs to another city, or to somebody else | 404 |
| `RESTAURANTS_UNAVAILABLE`, `PAYMENTS_UNAVAILABLE`, `STORE_UNAVAILABLE`, `DIRECTORY_UNAVAILABLE` | what the service depends on did not answer; nothing was stored; the caller may try again | 503 |
| `KEY_REQUIRED`, `KEY_REUSED` | an order without an `Idempotency-Key`, or the same key with another order | 400, 422 |
| `NOT_THE_MANAGER`, `NOT_THE_OWNER` | somebody of the same city who may see it and not change it | 403 |

## The scenarios

`scripts/scenarios.sh` tells them, and `scripts/scenarios.sh S8` tells one.

| | The story | What it shows |
|---|---|---|
| S0 | The platform stands | nine services behind a token; a token is refused where it was not issued for; only Access may use Keycloak's administration; Payments has no way in from outside |
| S1 | The journey of one order | five services, five transactions; the card's token is erased when it was used; the audit trail names the city and the actor of every step |
| S2 | The restaurant refuses | the money goes back, once |
| S3 | Nobody can carry it | the Kitchen stops, the money goes back |
| S4 | The bank refuses | the order is cancelled before anybody else heard of it |
| S5 | Twice is once | the same key: one order, one charge, the first answer again |
| S6 | Two cities | what belongs to one does not exist for the other, in five services |
| S7 | The customer changes their mind | until the restaurant cooks, and not after |
| S8 | Six orders, one courier | one goes, five are taken back and paid back |
| S9 | The payment provider does not answer | once: the client tries again under one key. For good: the order is told, the request waits in the dead-letter queue |
| S10 | Roles and cities | a role given at Access is in the next token, and felt at Dispatch; what is not an admin's to give |
| S11 | A picture | announced to Media, sent to the store, fetched from the store; what a file may be; run with two stores |
| S12 | Where is my order | positions as a time series; who may say and who may see |
| S13 | What happened | one notification, read in three languages |
| S14 | A service is down | Restaurants, the Kitchen and Notifications are stopped in the middle of an order: who asks is told, a message waits, a reader catches up |
| S15 | The restaurant never answers | with a deadline of eight seconds: the order is cancelled, stopped and paid back, and the customer told why; an order answered in time is left alone when its deadline arrives |
