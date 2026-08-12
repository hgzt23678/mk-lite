ARG KEYCLOAK_VERSION=26.7.0
FROM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION}

# Keep the credential boundary in Keycloak.  Only presentation resources are copied from
# the pinned Misskey 12.119.2 frontend; no application binary or secret enters this image.
COPY --chown=1000:0 deploy/pasture/keycloak/themes/activitypub-misskey-v12 \
    /opt/keycloak/themes/activitypub-misskey-v12
COPY --chown=1000:0 frontend/ActivityPub.Misskey.Blazor/wwwroot/css/misskey-v12-default-theme.css \
    /opt/keycloak/themes/activitypub-misskey-v12/login/resources/css/misskey-v12-default-theme.css
COPY --chown=1000:0 frontend/ActivityPub.Misskey.Blazor/wwwroot/css/misskey-v12-upstream.css \
    /opt/keycloak/themes/activitypub-misskey-v12/login/resources/css/misskey-v12-upstream.css
COPY --chown=1000:0 frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/fontawesome \
    /opt/keycloak/themes/activitypub-misskey-v12/login/resources/vendor/fontawesome
COPY --chown=1000:0 frontend/misskey-v12/public/static-assets/favicon.png \
    /opt/keycloak/themes/activitypub-misskey-v12/login/resources/img/favicon.png

USER 1000
