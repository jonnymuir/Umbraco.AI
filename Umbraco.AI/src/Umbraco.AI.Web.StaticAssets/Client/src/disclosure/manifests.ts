import type { ManifestGlobalContext } from "@umbraco-cms/backoffice/extension-registry";

const globalContextManifest: ManifestGlobalContext = {
    type: "globalContext",
    alias: "UmbracoAI.Disclosure.GlobalContext",
    name: "Umbraco AI Disclosure Global Context",
    api: () => import("./disclosure.context.js"),
};

export const disclosureManifests = [globalContextManifest];
