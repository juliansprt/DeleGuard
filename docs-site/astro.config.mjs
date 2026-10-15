// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightOpenAPI, { openAPISidebarGroups } from 'starlight-openapi';

export default defineConfig({
	site: 'http://localhost:4321',
	integrations: [
		starlight({
			title: 'Deleguard',
			description: 'Autorización determinista, aprobación humana y evidencia para acciones de agentes de IA.',
			logo: {
				src: './src/assets/brand-mark.png',
				alt: 'Deleguard',
			},
			favicon: '/favicon.png',
			defaultLocale: 'root',
			locales: {
				root: { label: 'Español', lang: 'es' },
				en: { label: 'English', lang: 'en' },
			},
			lastUpdated: true,
			pagination: true,
			customCss: ['./src/styles/deleguard.css'],
			sidebar: [
				{
					label: 'Empezar',
					translations: { en: 'Start here' },
					items: [
						{ slug: 'start/what-is-deleguard' },
						{ slug: 'start/quickstart' },
						{ slug: 'start/environment' },
					],
				},
				{
					label: 'Integrar',
					translations: { en: 'Integrate' },
					items: [
						{ slug: 'integrate/with-llm' },
						{ slug: 'integrate/overview' },
						{ slug: 'integrate/existing-apps' },
						{ slug: 'integrate/http' },
						{ slug: 'integrate/python' },
						{ slug: 'integrate/dotnet' },
						{ slug: 'integrate/mcp-guard' },
						{ slug: 'integrate/ide' },
						{ slug: 'integrate/errors' },
					],
				},
				{
					label: 'Consola',
					translations: { en: 'Console' },
					items: [
						{ slug: 'console/login' },
						{ slug: 'console/overview' },
						{ slug: 'console/approvals' },
						{ slug: 'console/playground' },
						{ slug: 'console/ai-gateway' },
                        { slug: 'console/agents' },
						{ slug: 'console/environments' },
						{ slug: 'console/actions' },
						{ slug: 'console/policies' },
						{ slug: 'console/audit' },
					],
				},
				{
					label: 'Referencia',
					translations: { en: 'Reference' },
					items: [
						{ slug: 'reference/decisions' },
						{ slug: 'reference/policy-yaml' },
						{ slug: 'reference/configuration' },
						{ slug: 'reference/admin-api' },
					],
				},
				{
					label: 'Conceptos',
					translations: { en: 'Concepts' },
					items: [
						{ slug: 'concepts/authorization-path' },
						{ slug: 'concepts/permits-evidence' },
						{ slug: 'concepts/tenants-environments' },
					],
				},
				...openAPISidebarGroups,
			],
			plugins: [
				starlightOpenAPI([
					{
						base: 'api',
						schema: '../deleguard_dev_pack/spec/openapi.yaml',
						sidebar: {
							label: 'OpenAPI',
							collapsed: true,
							operations: { badges: true, labels: 'operationId' },
						},
					},
				]),
			],
		}),
	],
});
