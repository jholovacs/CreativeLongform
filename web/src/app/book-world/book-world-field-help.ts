/**
 * Native `title` tooltips for Story & World form fields: expected content + example.
 * Keep strings concise; browsers truncate long titles.
 */
export const BOOK_WORLD_FIELD_HELP = {
  tone:
    'Overall narrative voice, mood, and prose style for prompts and consistency. Example: Literary realism with quiet tension; grounded dialogue.',
  contentNotes:
    'House style for scenes: pacing, POV, dialogue tags, what to avoid. Example: Prefer indirect speech; avoid italics for emphasis.',
  synopsis:
    'High-level plot summary for context and generation. Example: Two siblings reunite at a funeral and uncover a family secret spanning decades.',

  timelineSearch:
    'Filter timeline rows by text in titles or notes (case-insensitive). Example: coronation or flashback',
  timelineSortKey:
    'Decimal ordering along story-time (lower = earlier in the story). Use decimals to insert between beats. Example: 1000, 1000.5, 2000',
  timelineFxBase:
    'Name of the base currency or commodity in a pair at this beat. Example: Imperial crown',
  timelineFxQuote:
    'Name of the quote (counter) currency. Example: River guild mark',
  timelineFxAuthority:
    'Who sets or backs the rate (nation, bank, guild). Example: Imperial treasury',
  timelineFxNote:
    'In-world rate, spread, or story note for this exchange. Example: 1 crown ≈ 12 marks at the harbor',
  newTimelineTitle:
    'Short label for this world-only beat (not a manuscript scene). Example: Treaty signed at Elowen',
  newTimelineSummary:
    'Optional: what happens or why this beat matters in story-time. Example: Borders close; trade routes shift north.',
  newTimelineSortKey:
    'Optional decimal position; leave blank to append after the last entry. Example: 1500000 or 1500000.1',
  newTimelineWorldElement:
    'Optional link to a canon world element (e.g. a SignificantEvent). Pick from your world elements list.',
  newTimelineCurrencyBase: 'Same as Base above, for a new world event row. Example: Crown',
  newTimelineCurrencyQuote: 'Same as Quote above. Example: Guild mark',
  newTimelineCurrencyAuthority: 'Same as Authority above. Example: Central bank of Alder',
  newTimelineExchangeNote: 'Same as Exchange note above. Example: Black-market rate doubles in wartime',

  timelinePageSize: 'How many timeline rows to load per page from the server. Example: 20',

  measurementPreset:
    'Which Earth-like measurement bundle seeds prompts: metric/SI, US customary, or fully custom rows below. Example: Custom for a fantasy metals system.',
  calDaysPerYear:
    'Fictional calendar: days in one year (leave blank to use preset). Example: 400',
  calDaysPerWeek:
    'Fictional calendar: days in one week. Example: 8',
  monthNamesCsv:
    'Comma-separated month names in calendar order. Example: Frostreap, Greening, Highsun',
  weekdayNamesCsv:
    'Comma-separated weekday names. Example: Sun, Moon, Forge, Rest',

  unitsSearch:
    'Filter custom unit rows by category, name, symbol, definition, or SI note (client-side). Example: league',
  unitCategory:
    'Group for prompts (length, mass, currency, etc.). Example: Length',
  unitName:
    'Human-readable unit name. Example: league (walking)',
  unitSymbol:
    'Abbreviation or symbol. Example: lw',
  unitDefinition:
    'What the unit measures and how big it is in plain language. Example: Distance a healthy adult walks in one hour on flat road.',
  unitSiAnchor:
    'Optional rough SI or Earth comparison for the model. Example: ≈ 5 km',

  moneySearch:
    'Filter currency rows by name, authority, or definition (client-side). Example: doubloon',
  moneyName:
    'In-world currency name. Example: Alder doubloon',
  moneyAuthority:
    'Issuer or regime (nation, bank, guild). Example: Kingdom of Alder mint',
  moneyDefinition:
    'What counts as one unit and typical usage. Example: Gold coin; major trade settlements.',

  measurementNotes:
    'Optional free-form notes on money/units for prompts. Example: Only imperial coins are legal tender in the capital.',

  extractText:
    'Paste prose or notes; the model extracts structured world entries. Example: A paragraph describing a city’s wards and guilds.',
  generatePrompt:
    'Instructions for new world elements to generate. Example: Add two rival factions that control river trade.',

  elementsSearch:
    'Server search across element title, kind, summary, and detail. Example: Geography river',

  glossaryUseLlm:
    'When on, the model suggests additional alternate names for glossary entries (metadata and slug still used). Turn off for a faster export without extra LLM calls.',
  glossaryDownload:
    'Builds a Markdown glossary of all world elements (A–Z, ignoring leading “a/an/the” for order) and saves it as a .md file.',

  worldElementKind:
    'Category of entry (Geography, Lore, Character, …). Example: Geography for a city; Lore for a myth.',
  worldElementTitle:
    'Primary display name. Example: Harbor of Saltmere',
  worldElementSlug:
    'Optional URL-safe id (letters, numbers, hyphens). Example: harbor-saltmere',
  worldElementSummary:
    'Short blurb for lists and prompts (one to three sentences). Example: Busy port; smuggling hub.',
  worldElementDetail:
    'Longer canon: history, sensory detail, relationships. Example: The harbor has three basins; the east pier is controlled by…',
  worldElementStatus:
    'Draft = work in progress; Canon = established for the story. Example: Canon once reviewed.',

  linksSearch:
    'Search by from-title, to-title, or relation label (server-side). Example: located',

  linkFrom:
    'Source world element of the directed relation. Example: Character: Captain Mara',
  linkTo:
    'Target world element. Example: Geography: Ironport docks',
  linkLabel:
    'Relation in plain language (stored as relation label). Example: Located in or Reports to',

  suggestedLinkCheckbox:
    'Include this suggested link when you create selected links. Uncheck to skip this pair.'
} as const;
