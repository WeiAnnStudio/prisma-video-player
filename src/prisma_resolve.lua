-- SPDX-License-Identifier: GPL-3.0-or-later
-- Prisma -> DaVinci Resolve: adds review notes as markers to the timeline that is open.
-- Built on an earlier marker import script, with these changes:
--   * the notes come from a data file written by Prisma, never from code;
--   * the frame rate of the notes must match the timeline or nothing is imported;
--   * importing twice never duplicates: each note carries its Prisma id;
--   * positions are frames from the start of the timeline, also at 23.976 or 29.97.
-- Prisma runs it with fuscript.exe (Resolve Studio, external scripting set to Local).
-- PRISMA_MARKERS names the data file and PRISMA_RESULT the file for the outcome.

local M = {}

local function unescape(text)
    return (text:gsub("\\(.)", function(c)
        if c == "t" then return "\t" elseif c == "n" then return "\n" elseif c == "r" then return "\r" end
        return c
    end))
end

local function split(line)
    local fields = {}
    for field in (line .. "\t"):gmatch("([^\t]*)\t") do fields[#fields + 1] = field end
    return fields
end

function M.read_data(path)
    local file = io.open(path, "rb")
    if not file then return nil, "No se encontró el archivo de notas." end
    local content = file:read("*a")
    file:close()
    if #content > 16 * 1024 * 1024 then return nil, "El archivo de notas es demasiado grande." end
    local data = { markers = {} }
    local first = true
    for line in content:gmatch("([^\n]*)\n") do
        line = line:gsub("\r$", "")
        if first then
            if line ~= "PRISMA-MARKERS 1" then return nil, "Formato de notas desconocido." end
            first = false
        elseif line ~= "" then
            local f = split(line)
            if f[1] == "fps" then
                data.num, data.den = tonumber(f[2]), tonumber(f[3])
            elseif f[1] == "video" then
                data.video = unescape(f[2] or "")
            elseif f[1] == "m" then
                local frame = tonumber(f[2])
                if not frame or frame < 0 or frame ~= math.floor(frame) or not f[3] or f[3] == "" then return nil, "Hay una nota con datos no válidos." end
                data.markers[#data.markers + 1] = { frame = frame, id = unescape(f[3]), name = unescape(f[4] or ""), note = unescape(f[5] or "") }
            end
        end
    end
    if not data.num or not data.den or data.num <= 0 or data.den <= 0 then return nil, "Faltan los FPS de las notas." end
    return data
end

local function timeline_fps(timeline)
    local value = timeline:GetSetting("timelineFrameRate")
    if type(value) == "number" then return value end
    return tonumber(tostring(value or ""):match("[%d%.]+"))
end

local function frame_key(frame) return math.floor(tonumber(frame) + 0.5) end

-- Returns a table: status = OK | NO_RESOLVE | NO_PROJECT | NO_TIMELINE | FPS | DATA, plus counters.
function M.import(resolve, data)
    if not resolve then return { status = "NO_RESOLVE" } end
    local product = resolve.GetProductName and resolve:GetProductName() or ""
    if product ~= "" and not product:find("Studio", 1, true) then return { status = "NOT_STUDIO", product = product } end
    local manager = resolve:GetProjectManager()
    local project = manager and manager:GetCurrentProject()
    if not project then return { status = "NO_PROJECT", product = product } end
    local timeline = project:GetCurrentTimeline()
    if not timeline then return { status = "NO_TIMELINE", product = product } end

    local result = { status = "OK", product = product, timeline = timeline:GetName() or "", added = 0, present = 0, moved = 0, outside = 0, failed = 0 }
    local fps = timeline_fps(timeline)
    local notes_fps = data.num / data.den
    result.timeline_fps = fps and string.format("%.3f", fps) or "?"
    result.notes_fps = string.format("%.3f", notes_fps)
    if not fps or math.abs(fps - notes_fps) > 0.01 then result.status = "FPS"; return result end

    local length = timeline:GetEndFrame() - timeline:GetStartFrame()
    local taken = {}
    for frame, info in pairs(timeline:GetMarkers() or {}) do taken[frame_key(frame)] = info end

    for _, m in ipairs(data.markers) do
        local custom = "prisma:" .. m.id
        local existing = timeline:GetMarkerByCustomData(custom)
        local same = taken[m.frame]
        if (existing and next(existing) ~= nil) or (same and same.name == m.name and (same.note or "") == m.note) then
            result.present = result.present + 1
        elseif m.frame >= length then
            result.outside = result.outside + 1
        else
            local frame = m.frame
            while taken[frame] and frame < length - 1 and frame - m.frame < 250 do frame = frame + 1 end
            if taken[frame] or frame >= length then
                result.failed = result.failed + 1
            elseif timeline:AddMarker(frame, "Red", m.name, m.note, 1, custom) then
                taken[frame] = { name = m.name, note = m.note, customData = custom }
                result.added = result.added + 1
                if frame ~= m.frame then result.moved = result.moved + 1 end
            else
                result.failed = result.failed + 1
            end
        end
    end
    return result
end

function M.write_result(path, result)
    local file = io.open(path, "wb")
    if not file then return end
    local keys = { "status", "message", "product", "timeline", "timeline_fps", "notes_fps", "added", "present", "moved", "outside", "failed" }
    for _, key in ipairs(keys) do
        if result[key] ~= nil then file:write(key, "\t", (tostring(result[key]):gsub("[\t\r\n]", " ")), "\n") end
    end
    file:close()
end

function M.connect()
    local ok, app = pcall(function()
        if type(Resolve) == "function" then return Resolve() end
        if bmd and bmd.scriptapp then return bmd.scriptapp("Resolve") end
        return nil
    end)
    if ok then return app end
    return nil
end

function M.main()
    local data_path, result_path = os.getenv("PRISMA_MARKERS"), os.getenv("PRISMA_RESULT")
    if not data_path or not result_path then return end
    local data, err = M.read_data(data_path)
    local result
    if not data then
        result = { status = "DATA", message = err }
    else
        local ok, value = pcall(M.import, M.connect(), data)
        result = ok and value or { status = "ERROR", message = tostring(value) }
    end
    M.write_result(result_path, result)
end

-- fuscript runs each file in its own environment, so a test cannot signal itself with a
-- global: the import only runs when Prisma provides the data file.
if os.getenv("PRISMA_MARKERS") then M.main() end
return M
