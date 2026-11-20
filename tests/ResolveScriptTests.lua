-- SPDX-License-Identifier: GPL-3.0-or-later
-- Tests for src/prisma_resolve.lua against a simulated Resolve timeline.
-- Run from the project folder: fuscript.exe tests\ResolveScriptTests.lua (or luajit).
local root = (arg and arg[1]) or "."
local M = dofile(root .. "/src/prisma_resolve.lua")
local passed = 0
local function check(value, name)
    if not value then error("FAIL: " .. name) end
    print("PASS: " .. name); passed = passed + 1
end

local function timeline(fps, length, markers)
    local t = { markers = markers or {}, fps = fps, length = length }
    function t:GetName() return "Montaje" end
    function t:GetSetting(key) if key == "timelineFrameRate" then return self.fps end end
    function t:GetStartFrame() return 86400 end
    function t:GetEndFrame() return 86400 + self.length end
    function t:GetMarkers() local copy = {} for k, v in pairs(self.markers) do copy[k] = v end return copy end
    function t:GetMarkerByCustomData(custom) for _, v in pairs(self.markers) do if v.customData == custom then return v end end return {} end
    function t:AddMarker(frame, color, name, note, duration, custom)
        if self.markers[frame] then return false end
        self.markers[frame] = { color = color, name = name, note = note, duration = duration, customData = custom }; return true
    end
    return t
end
local function resolve(tl, product)
    local project = { GetCurrentTimeline = function() return tl end }
    local manager = { GetCurrentProject = function() return tl and project or nil end }
    return { GetProductName = function() return product or "DaVinci Resolve Studio" end, GetProjectManager = function() return manager end }
end
local function write(path, text) local f = assert(io.open(path, "wb")); f:write(text); f:close() end

local tmp = root .. "/tests/lua-datos.tmp"
write(tmp, "PRISMA-MARKERS 1\nfps\t24000\t1001\nvideo\tCorto 12.mov\n" ..
    "m\t0\tid0\tInicio\tRevisar negros\n" ..
    "m\t3507\tid1\tCarro\tSe ha colado una \\\"n\\\"\\tcon tab\\nsegunda línea\n" ..
    "m\t3508\tid2\tSonido\tLimpiar\n" ..
    "m\t99999\tid3\tFuera\tMás allá del final\n")
local data = assert(M.read_data(tmp))
check(#data.markers == 4 and data.num == 24000 and data.den == 1001 and data.video == "Corto 12.mov", "Lee el archivo de notas")
check(data.markers[2].note == "Se ha colado una \"n\"\tcon tab\nsegunda línea", "Conserva comillas, tabuladores, saltos y acentos")

local tl = timeline("23.976", 20000, { [3508] = { name = "Otro marcador", note = "", customData = "" } })
local r = M.import(resolve(tl), data)
check(r.status == "OK" and r.added == 3 and r.outside == 1 and r.moved == 1, "Importa, aparta el fotograma ocupado y descarta lo que queda fuera")
check(tl.markers[0].name == "Inicio" and tl.markers[0].color == "Red" and tl.markers[3509].customData == "prisma:id2", "Marcadores rojos con la identidad de Prisma")
local again = M.import(resolve(tl), data)
check(again.added == 0 and again.present == 3, "Importar dos veces no duplica")

check(M.import(resolve(timeline(25, 20000)), data).status == "FPS", "FPS distintos: no importa nada")
check(M.import(resolve(timeline("29.97 DF", 20000)), (function() write(tmp, "PRISMA-MARKERS 1\nfps\t30000\t1001\nm\t5\tx\ta\tb\n"); return M.read_data(tmp) end)()).status == "OK", "Acepta 29,97 DF")
check(M.import(resolve(nil), data).status == "NO_TIMELINE" or M.import(resolve(nil), data).status == "NO_PROJECT", "Sin línea de tiempo abierta")
check(M.import(nil, data).status == "NO_RESOLVE", "Sin conexión con Resolve")
check(M.import(resolve(timeline("23.976", 20000), "DaVinci Resolve"), data).status == "NOT_STUDIO", "Detecta la versión gratuita por el nombre que da Resolve")
write(tmp, "otra cosa\n"); check(M.read_data(tmp) == nil, "Rechaza archivos que no son de Prisma")
write(tmp, "PRISMA-MARKERS 1\nfps\t25\t1\nm\t-4\tx\ta\tb\n"); check(M.read_data(tmp) == nil, "Rechaza fotogramas negativos")

local out = root .. "/tests/lua-resultado.tmp"
M.write_result(out, { status = "OK", timeline = "Línea\tcon\ntab", added = 3 })
local f = io.open(out, "rb"); local text = f:read("*a"); f:close()
check(text == "status\tOK\ntimeline\tLínea con tab\nadded\t3\n", "Escribe el resultado en una línea por dato")
os.remove(tmp); os.remove(out)
print(passed .. " comprobaciones correctas.")
