% analyze_danmaku_burst_map.m
% Generate esports danmaku burst-map deliverables for the SURF project.

clear; clc;

rootDir = fileparts(fileparts(mfilename("fullpath")));
danmuDir = fullfile(rootDir, "Danmu");
outDir = fullfile(rootDir, "outputs", "danmaku_burst_map");
if ~exist(outDir, "dir")
    mkdir(outDir);
end

xmlFiles = dir(fullfile(danmuDir, "*KT vs T1*第五局*.xml"));
if isempty(xmlFiles)
    error("Cannot find KT vs T1 XML file in %s", danmuDir);
end
xmlPath = fullfile(xmlFiles(1).folder, xmlFiles(1).name);
jsonPath = fullfile(danmuDir, "filtered_danmaku.json");
if ~isfile(jsonPath)
    error("Cannot find filtered danmaku JSON: %s", jsonPath);
end

fprintf("Reading XML: %s\n", xmlPath);
[rawTimes, rawTexts] = readBilibiliXml(xmlPath);
fprintf("Raw XML danmaku count: %d\n", numel(rawTimes));

fprintf("Reading JSON: %s\n", jsonPath);
collection = jsondecode(fileread(jsonPath));
entries = collection.entries;
filteredTimes = double([entries.timeSeconds]);
filteredTexts = string({entries.text});
if isfield(entries, "priorityScore")
    priorityScores = double([entries.priorityScore]);
else
    priorityScores = ones(size(filteredTimes));
end

density5 = buildDensityTable(rawTimes, 5);
density10 = buildDensityTable(rawTimes, 10);
writetable(density5, fullfile(outDir, "esports_density_5s.csv"), "Encoding", "UTF-8");
writetable(density10, fullfile(outDir, "esports_density_10s.csv"), "Encoding", "UTF-8");

burstInfo = detectBursts(density5, 10, 10);
burstInfo.source.xml_file = xmlPath;
burstInfo.source.filtered_json_file = jsonPath;
burstInfo.source.window_seconds = 5;
burstInfo.source.threshold_method = "max(P95, mean + 2*std)";

bursts = burstInfo.bursts;
characterRows = table();
eventRows = table();
allKeywordRows = table();

for i = 1:numel(bursts)
    b = bursts(i);
    contextStart = max(0, b.start_seconds - 10);
    contextEnd = b.end_seconds + 15;
    inWindow = filteredTimes >= contextStart & filteredTimes <= contextEnd;
    texts = filteredTexts(inWindow);
    scores = priorityScores(inWindow);

    [dominantEmotion, emotionTerms, emotionComments, emotionCounts] = classifyEmotion(texts);
    [contentMix, contentTerms, contentCounts] = classifyContent(texts);
    reps = representativeComments(texts, scores, 5);
    topTerms = extractTopTerms(texts, 8);
    [topicLabel, topicEvidence] = inferTopicLabel(topTerms, reps, texts);
    burstKind = inferBurstKind(b.start_seconds, topicLabel, contentMix, topTerms, reps, burstInfo.stats.opening_guard_seconds);

    bursts(i).filtered_count = numel(texts);
    bursts(i).topic_label = topicLabel;
    bursts(i).topic_evidence = topicEvidence;
    bursts(i).burst_kind = burstKind;
    bursts(i).dominant_emotion = dominantEmotion;
    bursts(i).emotion_evidence_terms = joinEvidenceTerms(emotionTerms);
    bursts(i).emotion_evidence_comments = joinComments(emotionComments);
    bursts(i).content_mix = contentMix;
    bursts(i).content_evidence_terms = joinEvidenceTerms(contentTerms);
    bursts(i).representative_comments = joinComments(reps);
    bursts(i).manual_check_note = "对应比赛画面事件待人工回看确认";

    eventRows = [eventRows; table( ...
        string(bursts(i).id), string(bursts(i).time_label), ...
        bursts(i).start_seconds, bursts(i).end_seconds, bursts(i).peak_seconds, ...
        bursts(i).peak_raw_count_5s, bursts(i).duration_seconds, bursts(i).baseline_multiplier, ...
        bursts(i).filtered_count, string(burstKind), string("待人工回看确认"), string("待人工回看确认"), ...
        'VariableNames', ["burst_id","time_range","start_seconds","end_seconds","peak_seconds", ...
        "peak_raw_count_5s","duration_seconds","baseline_multiplier","filtered_count","burst_kind","event_label","event_type"])]; %#ok<AGROW>

    characterRows = [characterRows; table( ...
        string(bursts(i).id), string(bursts(i).time_label), ...
        string(sprintf("%d 条/5s", bursts(i).peak_raw_count_5s)), ...
        string(sprintf("%.0f s", bursts(i).duration_seconds)), ...
        string(topicLabel), string(topicEvidence), ...
        string(dominantEmotion), string(joinEvidenceTerms(emotionTerms)), string(joinComments(emotionComments)), ...
        string(contentMix), string(joinEvidenceTerms(contentTerms)), string(joinComments(reps)), ...
        string("对应比赛画面事件待人工回看确认"), ...
        'VariableNames', ["burst_id","time_range","peak_density","duration","topic_label","topic_evidence","dominant_emotion", ...
        "emotion_evidence_terms","emotion_evidence_comments","content_mix","content_evidence_terms", ...
        "representative_comments","manual_check_note"])]; %#ok<AGROW>

    if ~isempty(topTerms)
        topTerms = topTerms(1:min(height(topTerms), 6), :);
        topTerms.burst_id = repmat(string(bursts(i).id), height(topTerms), 1);
        allKeywordRows = [allKeywordRows; topTerms(:, ["burst_id","term","count"])]; %#ok<AGROW>
    end
end

burstInfo.bursts = bursts;
writetable(eventRows, fullfile(outDir, "esports_burst_events.csv"), "Encoding", "UTF-8");
writetable(characterRows, fullfile(outDir, "esports_burst_characterization.csv"), "Encoding", "UTF-8");
writeTextFile(fullfile(outDir, "esports_burst_events.json"), jsonencode(burstInfo, "PrettyPrint", true));

makeDensityChart(density5, bursts, burstInfo.stats.candidate_threshold, fullfile(outDir, "esports_density_curve_5s.png"));
makeBurstBarChart(eventRows, fullfile(outDir, "esports_burst_bar_chart.png"));
makeBurstDurationChart(eventRows, fullfile(outDir, "esports_burst_duration_chart.png"));
makeKeywordChart(allKeywordRows, fullfile(outDir, "esports_keyword_evidence_chart.png"));
makeSummaryChart(characterRows, fullfile(outDir, "esports_emotion_content_summary.png"));

md = buildFeishuMarkdown(burstInfo, eventRows, characterRows);
writeTextFile(fullfile(outDir, "esports_feishu_insert.md"), md);

fprintf("Done. Deliverables written to: %s\n", outDir);

function [times, texts] = readBilibiliXml(xmlPath)
    doc = xmlread(xmlPath);
    nodes = doc.getElementsByTagName("d");
    n = nodes.getLength();
    times = zeros(n, 1);
    texts = strings(n, 1);
    keep = false(n, 1);

    for idx = 1:n
        node = nodes.item(idx - 1);
        p = char(node.getAttribute("p"));
        parts = split(string(p), ",");
        if numel(parts) < 1
            continue;
        end
        t = str2double(parts(1));
        if isnan(t) || t < 0
            continue;
        end
        times(idx) = t;
        texts(idx) = cleanText(string(char(node.getTextContent())));
        keep(idx) = true;
    end
    times = times(keep);
    texts = texts(keep);
end

function tbl = buildDensityTable(times, winSeconds)
    maxT = ceil(max(times) / winSeconds) * winSeconds;
    edges = 0:winSeconds:(maxT + winSeconds);
    counts = histcounts(times, edges)';
    starts = edges(1:end-1)';
    ends = edges(2:end)';
    labels = strings(numel(starts), 1);
    for i = 1:numel(starts)
        labels(i) = secondsLabel(starts(i)) + "-" + secondsLabel(ends(i));
    end
    tbl = table(starts, ends, labels, counts, counts ./ winSeconds, ...
        'VariableNames', ["window_start_seconds","window_end_seconds","time_label","raw_count","count_per_second"]);
end

function info = detectBursts(density, mergeGapSeconds, maxBursts)
    counts = double(density.raw_count);
    starts = double(density.window_start_seconds);
    durationSeconds = double(max(density.window_end_seconds));
    openingGuardSeconds = min(60, max(0, durationSeconds * 0.02));
    eligible = counts(starts >= openingGuardSeconds);
    if isempty(eligible)
        eligible = counts;
    end
    meanCount = mean(eligible);
    stdCount = std(eligible);
    medianCount = median(eligible);
    madCount = median(abs(eligible - medianCount));
    robustSigma = 1.4826 * madCount;
    p90 = percentileValue(eligible, 90);
    p95 = percentileValue(eligible, 95);
    p99 = percentileValue(eligible, 99);

    candidateThreshold = max(p90, medianCount + 3 * robustSigma);
    strongThreshold = max(p95, medianCount + 6 * robustSigma);
    hot = find(counts >= candidateThreshold);

    groups = groupHotWindows(hot, density, mergeGapSeconds);
    secondaryHot = find(counts >= p90);
    secondaryGroups = groupHotWindows(secondaryHot, density, mergeGapSeconds);
    groups = mergeGroupLists(groups, secondaryGroups);

    bursts = struct([]);
    for g = 1:numel(groups)
        idx = groups{g};
        [peakCount, localPeak] = max(counts(idx));
        peakIdx = idx(localPeak);
        startS = double(density.window_start_seconds(idx(1)));
        endS = double(density.window_end_seconds(idx(end)));
        peakS = double(density.window_start_seconds(peakIdx));
        bursts(g).id = "";
        bursts(g).start_seconds = startS;
        bursts(g).end_seconds = endS;
        bursts(g).peak_seconds = peakS;
        bursts(g).time_label = secondsLabel(startS) + "-" + secondsLabel(endS);
        bursts(g).peak_raw_count_5s = double(peakCount);
        bursts(g).duration_seconds = endS - startS;
        bursts(g).total_raw_count = double(sum(counts(idx)));
        bursts(g).baseline_multiplier = double(peakCount / max(meanCount, 1e-6));
        bursts(g).is_opening_peak = startS < openingGuardSeconds;
        bursts(g).event_label = "待人工回看确认";
        bursts(g).event_type = "待人工回看确认";
    end

    if ~isempty(bursts)
        openingPenalty = double([bursts.is_opening_peak]) * max([bursts.peak_raw_count_5s]) * 2;
        rankingScore = [bursts.peak_raw_count_5s] - openingPenalty;
        [~, orderByPeak] = sort(rankingScore, "descend");
        keep = orderByPeak(1:min(maxBursts, numel(orderByPeak)));
        bursts = bursts(keep);
        [~, chrono] = sort([bursts.start_seconds]);
        bursts = bursts(chrono);
        for i = 1:numel(bursts)
            bursts(i).id = "E-" + i;
        end
    end

    info.source = struct();
    info.stats = struct( ...
        "raw_total_count", sum(counts), ...
        "mean_5s_count", meanCount, ...
        "std_5s_count", stdCount, ...
        "median_5s_count", medianCount, ...
        "mad_5s_count", madCount, ...
        "p90_5s_count", p90, ...
        "p95_5s_count", p95, ...
        "p99_5s_count", p99, ...
        "opening_guard_seconds", openingGuardSeconds, ...
        "candidate_threshold", candidateThreshold, ...
        "burst_threshold", strongThreshold);
    info.bursts = bursts;
end

function groups = groupHotWindows(hot, density, mergeGapSeconds)
    groups = {};
    if ~isempty(hot)
        current = hot(1);
        for k = 2:numel(hot)
            prevEnd = density.window_end_seconds(current(end));
            nextStart = density.window_start_seconds(hot(k));
            if nextStart - prevEnd <= mergeGapSeconds
                current(end+1) = hot(k); %#ok<AGROW>
            else
                groups{end+1} = current; %#ok<AGROW>
                current = hot(k);
            end
        end
        groups{end+1} = current;
    end
end

function merged = mergeGroupLists(primary, secondary)
    merged = primary;
    for i = 1:numel(secondary)
        candidate = secondary{i};
        overlaps = false;
        for j = 1:numel(merged)
            if any(ismember(candidate, merged{j}))
                overlaps = true;
                break;
            end
        end
        if ~overlaps
            merged{end+1} = candidate; %#ok<AGROW>
        else
            for j = 1:numel(merged)
                if any(ismember(candidate, merged{j}))
                    merged{j} = unique([merged{j}, candidate]);
                    break;
                end
            end
        end
    end
end

function [label, evidenceTerms, evidenceComments, counts] = classifyEmotion(texts)
    rules = {
        "欢呼/激动", ["牛","赢","帅","神","漂亮","nb","NB","六冠","冠军","泪目","哭了","加油","起飞","燃","爽","恭喜"];
        "紧张/担忧", ["别","稳住","危险","完了","要寄","别急","吓","紧张","怕","悬","难了"];
        "嘲讽/调侃", ["笑死","哈哈","尴尬","送","菜","吓哭","回家","搞笑","小丑","急了","幽默"];
        "争议/愤怒", ["裁判","黑","凭什么","？？","不是","离谱","恶心","喷","骂","滚","傻","脑子"];
        "复盘/怀旧", ["去年","今年","最后","到此一游","离队","回看","纪录","再见","退役","历史","以前"];
        "弹幕行为", ["去人声","刷屏","举报","弹幕","带节奏","同一个人","人声","发弹幕"]
    };
    [label, evidenceTerms, counts] = classifyByRules(texts, rules);
    evidenceComments = commentsContaining(texts, evidenceTerms, 2);
end

function [mix, evidenceTerms, counts] = classifyContent(texts)
    rules = {
        "比赛事件反应", ["一血","击杀","团","开团","拆","基地","推","龙","大龙","小龙","先锋","加里奥","炸弹人","青钢影","选","ban","阵容","打野","中路","上路","下路"];
        "选手/队伍评价", ["Faker","faker","李哥","T1","KT","guma","GUMA","多兰","bdd","oner","宙斯","队","上单","打野","中单","辅助","选手"];
        "玩梗/黑话", ["大飞","飞科","捞","神","克苏鲁","皮肤","大运","节目","梗","享受","擎天巨像","兰子","OFGK","DOFGK"];
        "刷屏/弹幕行为", ["去人声","刷屏","举报","弹幕","带节奏","同一个人","人声","发送"];
        "赛后打卡/复盘", ["打卡","到此一游","去年","今年","最后","离队","回看","纪录","冠军","夺冠","助攻","再见"];
        "争议/对骂", ["裁判","黑","喷","骂","傻","脑子","恶心","不是","离谱","怪","节奏"]
    };
    [~, evidenceTerms, counts] = classifyByRules(texts, rules);
    total = max(sum(counts), 1);
    parts = strings(0);
    for i = 1:size(rules, 1)
        if counts(i) > 0
            parts(end+1) = rules{i, 1} + " " + sprintf("%.0f%%", counts(i) / total * 100); %#ok<AGROW>
        end
    end
    if isempty(parts)
        mix = "待人工复核";
    else
        mix = strjoin(parts, " / ");
    end
end

function [label, evidence] = inferTopicLabel(topTerms, reps, texts)
    terms = strings(0);
    if ~isempty(topTerms) && height(topTerms) > 0
        terms = string(topTerms.term);
    end
    joinedTerms = lower(strjoin(terms, " "));
    joinedComments = lower(strjoin(reps, " "));
    haystack = joinedTerms + " " + joinedComments;

    if containsAny(haystack, ["去人声","人声","刷屏","举报"]) || (containsAny(haystack, ["弹幕"]) && containsAny(haystack, ["带节奏"]))
        label = "“去人声”刷屏与弹幕秩序争论";
    elseif containsAny(haystack, ["多兰","兰子"]) && containsAny(haystack, ["找机会","开团","送","证明给我看","怪"])
        label = "多兰开团/找机会片段的复盘与调侃";
    elseif containsAny(haystack, ["加里奥","擎天巨像"]) || (containsAny(haystack, ["ban","放"]) && containsAny(haystack, ["kt","t1","faker"]))
        label = "KT 放加里奥与 Faker/T1 决胜局讨论";
    elseif containsAny(haystack, ["夺冠","冠军","六冠","助攻","捧杯","合影"])
        label = "T1 夺冠、纪录与赛后打卡复盘";
    elseif containsAny(haystack, ["guma","gumayusi","keria","fmvp","小k"])
        label = "T1 选手表现与队伍评价讨论";
    elseif containsAny(haystack, ["bdd","小火龙","bp","阵容","炸弹人"]) || (containsAny(haystack, ["选","ban"]) && containsAny(haystack, ["英雄","阵容","bdd","小火龙","炸弹人"]))
        label = "BP 阵容与 BDD/小火龙选择讨论";
    elseif containsAny(haystack, ["drx","s12","gemini","兵升","预判","补个妆"])
        label = "比赛细节与解说/选手小梗讨论";
    elseif containsAny(haystack, ["炸弹人","阵容","打野","选","ban"])
        label = "BP 阵容与英雄选择讨论";
    elseif containsAny(haystack, ["去年","今年","离队","再见","回看"])
        label = "跨年份回看与选手离队情绪";
    else
        if numel(terms) >= 3
            label = "围绕“" + strjoin(terms(1:3), " / ") + "”的集中讨论";
        elseif ~isempty(reps)
            label = "围绕代表弹幕的集中讨论";
        else
            label = "待人工复核主题";
        end
    end

    termPart = "高频词：" + joinEvidenceTerms(terms(1:min(numel(terms), 6)));
    matchedComments = commentsContaining(texts, terms(1:min(numel(terms), 4)), 2);
    if isempty(matchedComments)
        matchedComments = reps(1:min(numel(reps), 2));
    end
    commentPart = "代表弹幕：" + joinComments(matchedComments);
    evidence = termPart + "；" + commentPart;
end

function kind = inferBurstKind(startSeconds, topicLabel, contentMix, topTerms, reps, openingGuardSeconds)
    terms = strings(0);
    if ~isempty(topTerms) && height(topTerms) > 0
        terms = string(topTerms.term);
    end
    haystack = lower(strjoin([terms(:); reps(:); string(topicLabel)], " "));
    if startSeconds < openingGuardSeconds
        if containsAny(haystack, ["打卡","到此一游","来了","第一","考古","开头","补档"])
            kind = "开头观看行为峰（非比赛事件）";
        else
            kind = "开头聚集峰（需人工确认）";
        end
    elseif containsAny(haystack, ["去人声","人声","刷屏","举报","弹幕秩序"])
        kind = "弹幕/社群行为峰（非比赛事件）";
    elseif containsAny(lower(string(topicLabel)), ["多兰","bp","bdd","加里奥","选手表现","队伍评价"])
        kind = "比赛内容候选峰";
    elseif containsAny(haystack, ["夺冠","冠军","六冠","捧杯","合影"])
        kind = "赛果/夺冠情绪峰";
    elseif containsAny(haystack, ["打卡","到此一游","回看","离队","再见"])
        kind = "赛后回看/打卡峰";
    else
        kind = "比赛内容候选峰";
    end
end

function tf = containsAny(text, terms)
    tf = false;
    for i = 1:numel(terms)
        if contains(text, lower(terms(i)), "IgnoreCase", true)
            tf = true;
            return;
        end
    end
end

function [label, evidenceTerms, counts] = classifyByRules(texts, rules)
    counts = zeros(size(rules, 1), 1);
    termCounts = containers.Map("KeyType", "char", "ValueType", "double");
    for r = 1:size(rules, 1)
        terms = rules{r, 2};
        for t = 1:numel(terms)
            c = countContains(texts, terms(t));
            counts(r) = counts(r) + c;
            if c > 0
                termCounts(char(terms(t))) = c;
            end
        end
    end
    if all(counts == 0)
        label = "待人工复核";
        evidenceTerms = strings(0);
        return;
    end
    [~, best] = max(counts);
    label = string(rules{best, 1});
    keys = string(termCounts.keys)';
    vals = zeros(numel(keys), 1);
    for i = 1:numel(keys)
        vals(i) = termCounts(char(keys(i)));
    end
    [~, order] = sort(vals, "descend");
    order = order(1:min(numel(order), 8));
    evidenceTerms = keys(order) + "(" + string(vals(order)) + ")";
end

function c = countContains(texts, term)
    if isempty(texts)
        c = 0;
    else
        c = sum(contains(texts, term, "IgnoreCase", true));
    end
end

function reps = representativeComments(texts, scores, maxN)
    texts = texts(:);
    scores = scores(:);
    if isempty(texts)
        reps = strings(0);
        return;
    end
    lens = strlength(texts);
    score2 = scores + double(lens >= 4 & lens <= 60) * 1.5 - double(lens > 90) * 2;
    [~, order] = sort(score2, "descend");
    reps = strings(0);
    seen = strings(0);
    for i = 1:numel(order)
        txt = cleanText(texts(order(i)));
        key = regexprep(lower(txt), "\s+", "");
        if strlength(txt) < 2 || any(seen == key)
            continue;
        end
        reps(end+1) = txt; %#ok<AGROW>
        seen(end+1) = key; %#ok<AGROW>
        if numel(reps) >= maxN
            break;
        end
    end
end

function comments = commentsContaining(texts, evidenceTerms, maxN)
    comments = strings(0);
    if isempty(texts) || isempty(evidenceTerms)
        return;
    end
    rawTerms = regexprep(evidenceTerms, "\(\d+\)$", "");
    for i = 1:numel(texts)
        if any(contains(texts(i), rawTerms, "IgnoreCase", true))
            comments(end+1) = cleanText(texts(i)); %#ok<AGROW>
        end
        if numel(comments) >= maxN
            break;
        end
    end
end

function topTerms = extractTopTerms(texts, maxN)
    stop = ["这个","那个","就是","什么","不是","一个","一下","真的","感觉","还是","可以","没有","这么","怎么","已经","时候","哈哈","哈哈哈"];
    terms = strings(0);
    for i = 1:numel(texts)
        txt = cleanText(texts(i));
        asciiTokens = regexp(txt, "[A-Za-z][A-Za-z0-9_]{1,}", "match");
        cjkTokens = regexp(txt, "[\x{4e00}-\x{9fff}]{2,6}", "match");
        tokens = [string(asciiTokens), string(cjkTokens)];
        tokens = tokens(strlength(tokens) >= 2);
        tokens = tokens(~ismember(tokens, stop));
        terms = [terms, tokens]; %#ok<AGROW>
    end
    if isempty(terms)
        topTerms = table(strings(0,1), zeros(0,1), 'VariableNames', ["term","count"]);
        return;
    end
    [u, ~, ic] = unique(terms);
    counts = accumarray(ic(:), 1);
    [counts, order] = sort(counts, "descend");
    u = u(order);
    n = min(maxN, numel(u));
    topTerms = table(u(1:n)', counts(1:n), 'VariableNames', ["term","count"]);
end

function s = cleanText(s)
    s = string(s);
    s = replace(s, ["&amp;","&lt;","&gt;","&quot;","&apos;"], ["&","<",">","""","'"]);
    s = regexprep(s, "\s+", " ");
    s = strtrim(s);
end

function value = percentileValue(x, p)
    x = sort(double(x(:)));
    if isempty(x)
        value = NaN;
        return;
    end
    pos = (p / 100) * (numel(x) - 1) + 1;
    lo = floor(pos);
    hi = ceil(pos);
    if lo == hi
        value = x(lo);
    else
        value = x(lo) + (x(hi) - x(lo)) * (pos - lo);
    end
end

function label = secondsLabel(secondsValue)
    secondsValue = max(0, double(secondsValue));
    m = floor(secondsValue / 60);
    s = floor(mod(secondsValue, 60));
    label = string(sprintf("%02d:%02d", m, s));
end

function out = joinEvidenceTerms(terms)
    if isempty(terms)
        out = "无明显关键词命中，需人工复核";
    else
        out = strjoin(terms, "、");
    end
end

function out = joinComments(comments)
    if isempty(comments)
        out = "需人工补充代表弹幕";
    else
        safe = replace(comments, "|", "/");
        out = "「" + strjoin(safe, "」「") + "」";
    end
end

function writeTextFile(path, txt)
    fid = fopen(path, "w", "n", "UTF-8");
    if fid < 0
        error("Cannot write file: %s", path);
    end
    cleanup = onCleanup(@() fclose(fid));
    fprintf(fid, "%s", txt);
end

function makeDensityChart(density, bursts, threshold, outPath)
    fig = newFigure();
    plot(density.window_start_seconds ./ 60, density.raw_count, "LineWidth", 1.2, "Color", [0.05 0.25 0.55]);
    hold on;
    yline(threshold, "--", "爆发阈值", "Color", [0.75 0.1 0.1], "LineWidth", 1);
    for i = 1:numel(bursts)
        x = bursts(i).peak_seconds / 60;
        y = bursts(i).peak_raw_count_5s;
        scatter(x, y, 42, [0.85 0.1 0.1], "filled");
        text(x, y + 2, bursts(i).id, "HorizontalAlignment", "center", "FontWeight", "bold");
    end
    title("电竞样本弹幕密度曲线（5秒窗口）");
    xlabel("视频时间（分钟）");
    ylabel("弹幕数量 / 5秒");
    grid on;
    exportgraphics(fig, outPath, "Resolution", 180);
    close(fig);
end

function makeBurstBarChart(eventRows, outPath)
    fig = newFigure();
    bar(categorical(eventRows.burst_id), eventRows.peak_raw_count_5s, "FaceColor", [0.2 0.45 0.75]);
    title("各爆发事件峰值密度");
    xlabel("爆发事件");
    ylabel("峰值弹幕数量 / 5秒");
    grid on;
    exportgraphics(fig, outPath, "Resolution", 180);
    close(fig);
end

function makeBurstDurationChart(eventRows, outPath)
    fig = newFigure();
    bar(categorical(eventRows.burst_id), eventRows.duration_seconds, "FaceColor", [0.2 0.6 0.45]);
    title("各爆发事件持续时长");
    xlabel("爆发事件");
    ylabel("持续时长（秒）");
    grid on;
    exportgraphics(fig, outPath, "Resolution", 180);
    close(fig);
end

function makeKeywordChart(keywordRows, outPath)
    fig = newFigure();
    if isempty(keywordRows)
        text(0.5, 0.5, "无可用关键词", "HorizontalAlignment", "center");
        axis off;
    else
        [u, ~, ic] = unique(keywordRows.term);
        counts = accumarray(ic, keywordRows.count);
        [counts, order] = sort(counts, "descend");
        u = u(order);
        n = min(12, numel(u));
        barh(categorical(u(n:-1:1)), counts(n:-1:1), "FaceColor", [0.55 0.35 0.72]);
        title("爆发窗口真实高频词证据");
        xlabel("出现次数");
        ylabel("关键词");
        grid on;
    end
    exportgraphics(fig, outPath, "Resolution", 180);
    close(fig);
end

function makeSummaryChart(characterRows, outPath)
    fig = newFigure();
    tiledlayout(1, 2, "Padding", "compact", "TileSpacing", "compact");
    nexttile;
    [emo, ~, emoIdx] = unique(characterRows.dominant_emotion);
    emoCounts = accumarray(emoIdx, 1);
    pie(emoCounts);
    legend(emo, "Location", "southoutside");
    title("主导情绪分布");

    nexttile;
    primaryContent = strings(height(characterRows), 1);
    for i = 1:height(characterRows)
        part = split(string(characterRows.content_mix(i)), " ");
        primaryContent(i) = part(1);
    end
    [con, ~, conIdx] = unique(primaryContent);
    conCounts = accumarray(conIdx, 1);
    pie(conCounts);
    legend(con, "Location", "southoutside");
    title("主要内容类型分布");
    exportgraphics(fig, outPath, "Resolution", 180);
    close(fig);
end

function fig = newFigure()
    fig = figure("Visible", "off", "Color", "white", "Position", [100 100 1100 620]);
    set(fig, "DefaultAxesFontName", "Microsoft YaHei");
    set(fig, "DefaultTextFontName", "Microsoft YaHei");
end

function md = buildFeishuMarkdown(info, eventRows, characterRows)
    md = "";
    md = md + "### 3.1 密度曲线" + newline + newline;
    md = md + "[插入图：esports_density_curve_5s.png]" + newline + newline;
    md = md + sprintf("整体特征：本场电竞样本共解析 XML 原始弹幕 %d 条。考虑到 B 站回放弹幕可能在开头出现打卡/补档型虚高峰，算法用开头 %.0f 秒之后的数据估计稳健基线。5 秒窗口候选阈值为 %.1f 条/5s，强爆发阈值为 %.1f 条/5s，自动保留 %d 个爆发/高密度候选。", ...
        info.stats.raw_total_count, info.stats.opening_guard_seconds, info.stats.candidate_threshold, info.stats.burst_threshold, height(eventRows));
    md = md + newline + newline;
    md = md + "### 3.2 爆发时刻清单" + newline + newline;
    md = md + "| 编号 | 视频时间点 | 峰值密度 | 持续时长 | 峰值性质 | 对应事件 | 事件类型 |" + newline;
    md = md + "|---|---|---:|---:|---|---|---|" + newline;
    for i = 1:height(eventRows)
        md = md + sprintf("| %s | %s | %d 条/5s | %.0f s | %s | 待人工回看确认 | 待人工回看确认 |%s", ...
            eventRows.burst_id(i), eventRows.time_range(i), eventRows.peak_raw_count_5s(i), eventRows.duration_seconds(i), ...
            escapeMdCell(eventRows.burst_kind(i)), newline);
    end
    md = md + newline;
    md = md + "### 3.3 爆发时刻刻画表" + newline + newline;
    md = md + "| 编号 | 主题标签 | 主题依据 | 主导情绪 | 情绪依据 | 内容类型 | 内容依据 | 代表性弹幕 |" + newline;
    md = md + "|---|---|---|---|---|---|---|---|" + newline;
    for i = 1:height(characterRows)
        emoEvidence = "来自高频词：" + characterRows.emotion_evidence_terms(i) + "；代表弹幕：" + characterRows.emotion_evidence_comments(i);
        contentEvidence = "来自高频词：" + characterRows.content_evidence_terms(i);
        md = md + sprintf("| %s | %s | %s | %s | %s | %s | %s | %s |%s", ...
            characterRows.burst_id(i), ...
            escapeMdCell(characterRows.topic_label(i)), ...
            escapeMdCell(characterRows.topic_evidence(i)), ...
            escapeMdCell(characterRows.dominant_emotion(i)), ...
            escapeMdCell(emoEvidence), ...
            escapeMdCell(characterRows.content_mix(i)), ...
            escapeMdCell(contentEvidence), ...
            escapeMdCell(characterRows.representative_comments(i)), newline);
    end
    md = md + newline;
    md = md + "### 3.4 这个项目对设计的启示" + newline + newline;
    md = md + "- 电竞弹幕爆发不仅对应比赛事件，也可能对应刷屏、玩梗和赛后复盘，因此 VR 表达需要区分“比赛空间事件”和“社群行为事件”。" + newline;
    md = md + "- 爆发窗口内的代表性词汇和弹幕可以作为聚合显示内容，峰值时不宜逐条平铺全部文字。" + newline;
    md = md + "- 对含有“刷屏/去人声/带节奏”等证据的窗口，建议在 VR 中使用低侵入的群体状态提示，而不是绑定到球员或地图位置。" + newline;
    md = md + "- 对含有“夺冠/打卡/纪录/队伍选手名”等证据的窗口，适合转化为赛后纪念型或群体欢呼型可视化。" + newline;
    md = md + newline;
    md = md + "### 图片占位" + newline + newline;
    md = md + "- esports_density_curve_5s.png" + newline;
    md = md + "- esports_burst_bar_chart.png" + newline;
    md = md + "- esports_burst_duration_chart.png" + newline;
    md = md + "- esports_keyword_evidence_chart.png" + newline;
    md = md + "- esports_emotion_content_summary.png" + newline;
end

function out = escapeMdCell(s)
    out = replace(string(s), "|", "/");
    out = replace(out, newline, " ");
end
