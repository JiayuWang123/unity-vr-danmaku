function run_burst_map(inputXml, outputDir, configPath)
%RUN_BURST_MAP XML-only MATLAB danmaku burst-map analyzer.
%   run_burst_map(inputXml, outputDir, configPath) parses a Bilibili XML
%   danmaku file and writes normalized tables, burst tables, JSON, a markdown
%   report, and static PNG charts. configPath is accepted for API parity with
%   the Python implementation; this lightweight MATLAB version uses built-in
%   defaults.

if nargin < 2 || strlength(string(outputDir)) == 0
    outputDir = fullfile(pwd, "outputs", "danmaku_burst_map_generalized");
end
if nargin < 3
    configPath = "";
end %#ok<NASGU>

if ~isfolder(outputDir)
    mkdir(outputDir);
end

[entries, rawTimes] = parseBilibiliXml(inputXml);
density5 = buildDensity(rawTimes, 5);
density10 = buildDensity(rawTimes, 10);
[bursts, stats] = detectBursts(density5, 10, 10);
bursts = characterizeBursts(bursts, entries, stats);

writetable(entries, fullfile(outputDir, "normalized_danmaku.csv"), "Encoding", "UTF-8");
writetable(density5, fullfile(outputDir, "density_5s.csv"), "Encoding", "UTF-8");
writetable(density10, fullfile(outputDir, "density_10s.csv"), "Encoding", "UTF-8");
writetable(struct2table(bursts), fullfile(outputDir, "burst_events.csv"), "Encoding", "UTF-8");
writetable(struct2table(bursts), fullfile(outputDir, "burst_characterization.csv"), "Encoding", "UTF-8");
writeText(fullfile(outputDir, "burst_events.json"), jsonencode(struct("stats", stats, "bursts", bursts), "PrettyPrint", true));
writeReport(fullfile(outputDir, "analysis_report.md"), inputXml, stats, bursts);
makeCharts(outputDir, density5, bursts, stats);

fprintf("Generated %d burst events from %d XML danmaku entries.\n", numel(bursts), height(entries));
fprintf("Output: %s\n", outputDir);
end

function [entries, rawTimes] = parseBilibiliXml(inputXml)
doc = xmlread(inputXml);
nodes = doc.getElementsByTagName("d");
n = nodes.getLength();
rows = cell(n, 12);
valid = false(n, 1);
for i = 1:n
    node = nodes.item(i - 1);
    p = string(char(node.getAttribute("p")));
    parts = split(p, ",");
    if numel(parts) < 8
        continue;
    end
    t = str2double(parts(1));
    if isnan(t)
        continue;
    end
    raw = string(char(node.getTextContent()));
    clean = strtrim(regexprep(raw, "\s+", " "));
    norm = lower(regexprep(clean, "\s+", ""));
    mode = str2double(parts(2));
    fontSize = str2double(parts(3));
    priority = 1 + min(strlength(clean) / 12, 2) + double(strlength(clean) >= 4 && strlength(clean) <= 60);
    rows(i, :) = {t, mode, fontSize, string(parts(4)), string(parts(5)), string(parts(6)), string(parts(7)), string(parts(8)), raw, clean, norm, priority};
    valid(i) = true;
end
rows = rows(valid, :);
entries = cell2table(rows, 'VariableNames', {'time_seconds','mode','font_size','color','timestamp','pool','user_hash','danmaku_id','raw_text','clean_text','normalized_text','priority_score'});
entries = sortrows(entries, "time_seconds");
rawTimes = double(entries.time_seconds);
entries.text_length = strlength(string(entries.clean_text));
entries.is_symbol_only = matches(string(entries.clean_text), "^[\p{P}\p{S}\s]+$");
entries.is_duplicate_like = false(height(entries), 1);
entries.is_spam_like = false(height(entries), 1);
entries.evidence_weight = double(entries.priority_score);
end

function density = buildDensity(times, win)
maxT = ceil(max(times) / win) * win;
edges = 0:win:(maxT + win);
counts = histcounts(times, edges)';
starts = edges(1:end-1)';
ends = edges(2:end)';
labels = strings(numel(starts), 1);
for i = 1:numel(starts)
    labels(i) = secondsLabel(starts(i)) + "-" + secondsLabel(ends(i));
end
density = table(starts, ends, labels, counts, counts ./ win, 'VariableNames', {'window_start_seconds','window_end_seconds','time_label','raw_count','count_per_second'});
end

function [bursts, stats] = detectBursts(density, mergeGap, maxBursts)
counts = double(density.raw_count);
starts = double(density.window_start_seconds);
openingGuard = max(60, max(density.window_end_seconds) * 0.02);
eligible = counts(starts >= openingGuard);
if isempty(eligible), eligible = counts; end
med = median(eligible);
madValue = median(abs(eligible - med));
robustSigma = 1.4826 * madValue;
p90 = percentileValue(eligible, 90);
p95 = percentileValue(eligible, 95);
candidate = max(p90, med + 3 * robustSigma);
strong = max(p95, med + 6 * robustSigma);
hot = find(counts >= candidate);
groups = {};
if ~isempty(hot)
    current = hot(1);
    for i = 2:numel(hot)
        if density.window_start_seconds(hot(i)) - density.window_end_seconds(current(end)) <= mergeGap
            current(end+1) = hot(i); %#ok<AGROW>
        else
            groups{end+1} = current; %#ok<AGROW>
            current = hot(i);
        end
    end
    groups{end+1} = current;
end
bursts = struct([]);
for g = 1:numel(groups)
    idx = groups{g};
    [peak, local] = max(counts(idx));
    peakIdx = idx(local);
    bursts(g).burst_id = "";
    bursts(g).start_seconds = double(density.window_start_seconds(idx(1)));
    bursts(g).end_seconds = double(density.window_end_seconds(idx(end)));
    bursts(g).peak_seconds = double(density.window_start_seconds(peakIdx));
    bursts(g).time_range = secondsLabel(bursts(g).start_seconds) + "-" + secondsLabel(bursts(g).end_seconds);
    bursts(g).peak_density_5s = peak;
    bursts(g).duration_seconds = bursts(g).end_seconds - bursts(g).start_seconds;
    bursts(g).baseline_multiplier = peak / max(mean(eligible), 1e-6);
end
if ~isempty(bursts)
    [~, order] = sort([bursts.peak_density_5s], "descend");
    bursts = bursts(order(1:min(maxBursts, numel(order))));
    [~, chrono] = sort([bursts.start_seconds]);
    bursts = bursts(chrono);
    for i = 1:numel(bursts)
        bursts(i).burst_id = "B-" + i;
    end
end
stats = struct("raw_total_count", sum(counts), "median_5s_count", med, "mad_5s_count", madValue, "p90_5s_count", p90, "p95_5s_count", p95, "candidate_threshold", candidate, "strong_threshold", strong, "opening_guard_seconds", openingGuard);
end

function bursts = characterizeBursts(bursts, entries, stats)
for i = 1:numel(bursts)
    inWindow = entries.time_seconds >= max(0, bursts(i).start_seconds - 10) & entries.time_seconds <= bursts(i).end_seconds + 15;
    texts = string(entries.clean_text(inWindow));
    terms = topTerms(texts, 8);
    reps = representative(texts, 5);
    evidence = strjoin(terms, "; ");
    haystack = lower(strjoin([terms(:); reps(:)], " "));
    if containsAny(haystack, ["去人声","刷屏","举报","人声"])
        kind = "viewer_behavior_peak"; topic = "Viewer behavior and chat-order discussion"; emotion = "viewer_behavior";
    elseif containsAny(haystack, ["冠军","夺冠","六冠","纪录","捧杯"])
        kind = "result_or_celebration_peak"; topic = "Result, record, and celebration discussion"; emotion = "excitement";
    elseif containsAny(haystack, ["阵容","ban","BP","bp","选"])
        kind = "gameplay_peak"; topic = "Draft, tactics, and choice discussion"; emotion = "neutral_analysis";
    else
        kind = "gameplay_peak"; topic = "Gameplay or player-focused discussion"; emotion = "neutral_analysis";
    end
    if bursts(i).start_seconds < stats.opening_guard_seconds && containsAny(haystack, ["打卡","到此一游","补档","考古"])
        kind = "opening_artifact_peak";
    end
    bursts(i).burst_kind = kind;
    bursts(i).topic_label = topic;
    bursts(i).topic_confidence = 0.6;
    bursts(i).dominant_emotion = emotion;
    bursts(i).emotion_confidence = 0.6;
    bursts(i).content_mix = "evidence_based_topic 100%";
    bursts(i).content_confidence = 0.6;
    bursts(i).evidence_terms = evidence;
    bursts(i).evidence_comments = strjoin(reps(1:min(numel(reps), 3)), " | ");
    bursts(i).representative_comments = strjoin(reps, " | ");
end
end

function terms = topTerms(texts, n)
bag = strings(0);
for i = 1:numel(texts)
    bag = [bag, string(regexp(texts(i), "[A-Za-z][A-Za-z0-9_]{1,}|[\x{4e00}-\x{9fff}]{2,6}", "match"))]; %#ok<AGROW>
end
if isempty(bag)
    terms = strings(0);
    return;
end
[u, ~, ic] = unique(bag);
counts = accumarray(ic(:), 1);
[counts, order] = sort(counts, "descend");
u = u(order);
n = min(n, numel(u));
terms = u(1:n) + "(" + string(counts(1:n)') + ")";
end

function reps = representative(texts, n)
texts = unique(texts(strlength(texts) > 1), "stable");
[~, order] = sort(strlength(texts), "descend");
texts = texts(order);
n = min(n, numel(texts));
reps = replace(texts(1:n), "|", "/");
end

function makeCharts(outputDir, density, bursts, stats)
fig = figure("Visible", "off", "Color", "white", "Position", [100 100 1100 560]);
set(fig, "DefaultAxesFontName", "Microsoft YaHei");
set(fig, "DefaultTextFontName", "Microsoft YaHei");
plot(density.window_start_seconds ./ 60, density.raw_count, "LineWidth", 1.2); hold on;
xline(stats.opening_guard_seconds / 60, "--", "Opening guard");
yline(stats.candidate_threshold, "--", "Candidate threshold");
yline(stats.strong_threshold, "--", "Strong threshold");
for i = 1:numel(bursts)
    scatter(bursts(i).peak_seconds / 60, bursts(i).peak_density_5s, 40, "filled");
    text(bursts(i).peak_seconds / 60, bursts(i).peak_density_5s + 1, bursts(i).burst_id);
end
title("Danmaku Density Over Time (5s Window)");
xlabel("Video Time (minutes)");
ylabel("Danmaku Count / 5s");
grid on;
exportgraphics(fig, fullfile(outputDir, "density_curve_5s.png"), "Resolution", 180);
close(fig);
end

function writeReport(path, inputXml, stats, bursts)
[~, inputName, inputExt] = fileparts(string(inputXml));
inputLabel = inputName + inputExt;
lines = ["# Danmaku Burst Map Analysis Report", "", "Input XML: `" + inputLabel + "`", "", "## Density Baseline", "", "- Raw danmaku count: " + stats.raw_total_count, "- Candidate threshold: " + stats.candidate_threshold + " comments / 5s", "- Strong threshold: " + stats.strong_threshold + " comments / 5s", "", "## Burst Events", "", "| ID | Time Range | Peak / 5s | Kind | Topic |", "|---|---|---:|---|---|"];
for i = 1:numel(bursts)
    lines(end+1) = "| " + bursts(i).burst_id + " | " + bursts(i).time_range + " | " + bursts(i).peak_density_5s + " | " + bursts(i).burst_kind + " | " + bursts(i).topic_label + " |"; %#ok<AGROW>
end
writeText(path, strjoin(lines, newline));
end

function writeText(path, text)
fid = fopen(path, "w", "n", "UTF-8");
cleanup = onCleanup(@() fclose(fid)); %#ok<NASGU>
fprintf(fid, "%s", text);
end

function tf = containsAny(text, terms)
tf = false;
for i = 1:numel(terms)
    if contains(text, lower(terms(i)), "IgnoreCase", true)
        tf = true; return;
    end
end
end

function label = secondsLabel(secondsValue)
m = floor(secondsValue / 60);
s = floor(mod(secondsValue, 60));
label = string(sprintf("%02d:%02d", m, s));
end

function value = percentileValue(x, p)
x = sort(double(x(:)));
pos = (p / 100) * (numel(x) - 1) + 1;
lo = floor(pos); hi = ceil(pos);
if lo == hi
    value = x(lo);
else
    value = x(lo) + (x(hi) - x(lo)) * (pos - lo);
end
end
