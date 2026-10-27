// Check the embedded Jellyfin settings page for parse errors and duplicate control IDs.
const fs = require('node:fs');
const text = fs.readFileSync('Jellyfin.Plugin.AutoOrganize/Configuration/logsPage.html', 'utf8');
const scripts = [...text.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi)];
if (scripts.length !== 1) throw new Error('Expected a single inline settings script');
for (const [, code] of scripts) new Function(code);
const ids = [...text.matchAll(/\bid="([^"]+)"/g)].map(m => m[1]);
const duplicates = ids.filter((id, pos) => ids.indexOf(id) !== pos);
if (duplicates.length) throw new Error('Duplicate element IDs: ' + duplicates.join(', '));
for (const id of [
    'SeasonFolderTemplate', 'SeasonZeroFolderName', 'SeasonFolderPreview',
    'SeasonZeroFolderPreview', 'SeriesFolderTemplate', 'EpisodeFileTemplate',
    'TmdbApiKey', 'UseTmdbEpisodeTitles', 'SeriesPathPreview',
    'MovieFolderTemplate', 'MovieFileTemplate'
]) {
    if (!ids.includes(id)) throw new Error('Missing control ID: ' + id);
}
if ((text.match(/<details class="ao-collapse">/g) || []).length !== 4)
    throw new Error('Expected four collapsible template sections');
console.log('Settings JS syntax, unique IDs, 4 spoilers and required controls: OK');
