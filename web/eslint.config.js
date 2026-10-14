// @ts-check
const eslint = require("@eslint/js");
const { defineConfig } = require("eslint/config");
const tseslint = require("typescript-eslint");
const angular = require("angular-eslint");
const boundaries = require("eslint-plugin-boundaries");

module.exports = defineConfig([
  {
    files: ["**/*.ts"],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    plugins: { boundaries },
    settings: {
      // TypeScript dosyaları uzantısız import edilir; çözücüye bunu söylemezsek her bağımlılık
      // "unknown" görünür (eslint-import-resolver-node zaten kurulu, yeni paket gerekmiyor).
      "import/resolver": {
        node: { extensions: [".ts", ".js", ".json"] },
      },
      // docs/architecture.md, forbidden dependency 5: a feature never reaches into another
      // feature's files; sharing happens through core/ and shared/ only.
      "boundaries/elements": [
        { type: "core", pattern: "src/app/core" },
        { type: "shared", pattern: "src/app/shared" },
        { type: "feature", pattern: "src/app/features/*", capture: ["featureName"] },
        { type: "app", pattern: "src/app/*", mode: "file" },
        { type: "bootstrap", pattern: "src/*", mode: "file" },
      ],
      "boundaries/include": ["src/**/*.ts"],
    },
    rules: {
      "@angular-eslint/directive-selector": [
        "error",
        {
          type: "attribute",
          prefix: "app",
          style: "camelCase",
        },
      ],
      "@angular-eslint/component-selector": [
        "error",
        {
          type: "element",
          prefix: "app",
          style: "kebab-case",
        },
      ],
      // NOT: v7'nin "boundaries/dependencies" + "policies" biçimi denendi ve kural SESSİZCE
      // devre dışı kaldı (kasıtlı ihlal yeşil geçti). Kanıtlanabilir biçim korunuyor; eklentinin
      // kullanımdan kaldırma uyarıları bilinçli olarak kabul ediliyor - sessiz bir sınır kuralı,
      // gürültülü ama çalışan bir kuraldan kötüdür (docs/testing.md, yanlış yeşil).
      "boundaries/no-unknown": "error",
      "boundaries/no-unknown-files": "error",
      "boundaries/element-types": [
        "error",
        {
          default: "disallow",
          rules: [
            { from: ["bootstrap"], allow: ["app", "core", "shared", "feature"] },
            { from: ["app"], allow: ["app", "core", "shared", "feature"] },
            { from: ["core"], allow: ["core"] },
            { from: ["shared"], allow: ["core", "shared"] },
            {
              from: ["feature"],
              allow: [
                "core",
                "shared",
                // Only the feature's own files - never a sibling feature (forbidden dependency 5).
                ["feature", { featureName: "${from.featureName}" }],
              ],
            },
          ],
        },
      ],
    },
  },
  {
    files: ["**/*.html"],
    extends: [
      angular.configs.templateRecommended,
      angular.configs.templateAccessibility,
    ],
    rules: {},
  },
]);
