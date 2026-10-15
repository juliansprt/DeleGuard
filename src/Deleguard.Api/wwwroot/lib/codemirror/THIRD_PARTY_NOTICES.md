# Third-party notices for cm6.js

The bundled `cm6.js` includes the packages listed below. They are distributed
under the MIT license, with copyrights belonging to Marijn Haverbeke and the
other contributors identified in their original notices. Each linked file
preserves the package's complete original copyright and license text.

These notices apply to the third-party components in this bundle. They do not
grant a license to DeleGuard's own code.

| Package | Version | Original license text |
| --- | --- | --- |
| `@codemirror/autocomplete` | 6.18.6 | [MIT](licenses/codemirror-autocomplete.LICENSE.txt) |
| `@codemirror/commands` | 6.11.1 | [MIT](licenses/codemirror-commands.LICENSE.txt) |
| `@codemirror/lang-yaml` | 6.1.2 | [MIT](licenses/codemirror-lang-yaml.LICENSE.txt) |
| `@codemirror/language` | 6.12.4 | [MIT](licenses/codemirror-language.LICENSE.txt) |
| `@codemirror/lint` | 6.9.7 | [MIT](licenses/codemirror-lint.LICENSE.txt) |
| `@codemirror/search` | 6.7.2 | [MIT](licenses/codemirror-search.LICENSE.txt) |
| `@codemirror/state` | 6.7.5 | [MIT](licenses/codemirror-state.LICENSE.txt) |
| `@codemirror/view` | 6.43.12 | [MIT](licenses/codemirror-view.LICENSE.txt) |
| `@lezer/common` | 1.5.2 | [MIT](licenses/lezer-common.LICENSE.txt) |
| `@lezer/highlight` | 1.2.3 | [MIT](licenses/lezer-highlight.LICENSE.txt) |
| `@lezer/lr` | 1.4.10 | [MIT](licenses/lezer-lr.LICENSE.txt) |
| `@lezer/yaml` | 1.0.4 | [MIT](licenses/lezer-yaml.LICENSE.txt) |
| `@marijn/find-cluster-break` | 1.0.4 | [MIT](licenses/marijn-find-cluster-break.LICENSE.txt) |
| `codemirror` | 6.0.1 | [MIT](licenses/codemirror.LICENSE.txt) |
| `crelt` | 1.0.7 | [MIT](licenses/crelt.LICENSE.txt) |
| `style-mod` | 4.1.3 | [MIT](licenses/style-mod.LICENSE.txt) |
| `w3c-keyname` | 2.2.8 | [MIT](licenses/w3c-keyname.LICENSE.txt) |

The inventory was checked against the module markers in `cm6.js`, the local
package installation, and its lockfile. An offline esbuild 0.25.9 rebuild with
the same exports (`EditorState`, `EditorView`, `autocompletion`, `basicSetup`,
and `yaml`) matched the bundle byte for byte. The license files were copied
unchanged from that installation. esbuild is a build tool; its code is not
included in this browser bundle.

Bundle SHA-256:
`1b83d9473f1415086e46a4295ee0bb7524fdace87a55374bd7fd3245575cb9a0`.

Keep these license texts with any redistributed copy of `cm6.js`, and update
this inventory and the corresponding texts when rebuilding with new packages.
