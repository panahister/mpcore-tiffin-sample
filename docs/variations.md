# Variations

Tiffin has two switches. A variant is a switch on `main`, never a branch: both settings are run on every
change, so neither of them rots.

| Switch | Where | Settings | What changes |
|---|---|---|---|
| `MEDIA_STORE` | `infrastructure/.env` | `rustfs` (the default), `seaweedfs` | which store answers the S3 API on port `39000` |
| `EDGE_AUTH` | `infrastructure/.env` | `off` (the default), `keycloak` | whether Apache APISIX verifies a token itself, before the services verify it again |

```bash
MEDIA_STORE=seaweedfs scripts/up.sh
```

Then restart the Media service: it remembers that its bucket exists, and the other store has none yet.

## The store of Media

The Media service speaks the S3 API and nothing else. Its adapter is one file,
`media/src/Tiffin.Media.Infrastructure/Store/S3ObjectStore.cs`, and it is the same for every store.

| Store | Licence | State here |
|---|---|---|
| [RustFS](https://github.com/rustfs/rustfs) `1.0.0` | Apache-2.0 | **run**: scenario S11, 33 checks |
| [SeaweedFS](https://github.com/seaweedfs/seaweedfs) `4.47` | Apache-2.0 | **run**: scenario S11, 33 checks |
| Ceph, through its RADOS Gateway | LGPL | fits by standard, not run: a cluster of monitors, managers and storage daemons is more than a developer's machine should carry |
| Amazon S3 | | fits by standard, not run |
| MinIO | AGPL-3.0 | **not used, on purpose** |

**Why not MinIO.** Its open-source repository was put into maintenance in December 2025 and archived in
February 2026. It stopped publishing images in October 2025, in the middle of a security release, and the
last image it published carries a vulnerability of high severity that will not be patched there. A sample
that others learn from does not start them on a product that is no longer maintained.

**Neither store is without defects.** Both publish security advisories, several of them critical, and
both repair them: that is the difference. One of RustFS, of September 2026, concerns uploads over a signed
address, which is what Media uses. So Media does not rely on the store to enforce what was announced: it
asks the store what arrived and compares (rule M5), and an address is signed for ten minutes, for one key
and one type.

**What two stores prove.** That Media depends on a standard and not on a product. The day a store is
abandoned, as MinIO was, the change is a line in a compose file.

## The second wall at the edge

With `EDGE_AUTH=keycloak`, Apache APISIX verifies the signature of a token with the public keys of the
realm and answers 401 itself to a request without a token. The services verify in both cases: the edge
cannot know for which service a token was issued, nor what its holder may do there. A gateway that
verifies as well is one more wall, never the only one (MP Core ADR-007).

## What is not a switch

| | State |
|---|---|
| Kubernetes | fits by standard, not run. The services read their settings from the environment and answer `/health/live` and `/health/ready`, which is what a cluster asks of them |
| Another identity provider than Keycloak | the services need OpenID Connect and nothing of Keycloak. Access is the exception: its adapter speaks Keycloak's administration, and another provider is another adapter behind the same port |
