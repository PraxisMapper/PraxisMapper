--NOTE: on android, clearing app data doesnt' delete the database, just contents of it, apparently.
--Since the database for a county is so big, and mobile SQLite only opens 1 DB at once, I might
--need to have each function open and close the appropriate DB on each call.
require("helpers")

local sqlite3 = require("sqlite3") 
localDb = "" --SQLite only lets you use 1 DB at a time so may as well share the actual variable. Now that its small again, going to go back to copying it locally
local localDbPath = system.pathForFile("database.sqlite", system.DocumentsDirectory)
--dataDb = ""
--local dataDbPath = system.pathForFile("database.sqlite", system.ResourceDirectory)
local dbVersionID = 1

--The read-write one
function openLocal()
    localDb = sqlite3.open(localDbPath)
end

--the readonly one.
-- function openData()
--     localDb = sqlite3.open(dataDbPath)
-- end

local function closeDb()
    localDb:close()
end

function startDatabase()
    --localDb is the one we write data to.
    local path = system.pathForFile("database.sqlite", system.DocumentsDirectory)
    localDb = sqlite3.open(path)

    -- Handle the "applicationExit" event to close the database
    local function onSystemEvent(event)
        if (event.type == "applicationExit" and localDb:isopen()) then localDb:close() end
    end

    Runtime:addEventListener("system", onSystemEvent)
end

function upgradeDatabaseVersion(oldDBversion)
    --if oldDbVersion is nil, that should mean we're making the DB for the first time and can skip this step
    if (oldDBversion == nil or oldDBversion == dbVersionID) then return end

    if (oldDBversion < 1) then
        --do any scripting to match upgrade to version 1
        --which should be none, since that's the baseline for this feature.
    end

   Exec("UPDATE systemData SET dbVersionID = " .. dbVersionID)
end

function Query(sql)
    --print("querying " .. sql)
    results = {}
    local tempResults = localDb:rows(sql)
    --print("results got")

    for row in localDb:rows(sql) do
        table.insert(results, row) 
    end
    --if (debugDB) then dump(results) end
    return results --results is a table of tables EX {[1] : {[1] : 1}} for count(*) when there are results.
end

function SingleValueQuery(sql)
    local query = sql
    for i,row in ipairs(Query(query)) do
        if (#row == 1) then
            return row[1]
        else
            return 0
        end
    end
    return 0
end

function Exec(sql)
    results = {}
    local resultCode = localDb:exec(sql);

     if (resultCode == 0) then
         return 0
     end

    --now its all error tracking.
     local errormsg = localDb:errmsg()
     print(errormsg)
     native.showAlert("dbExec error", errormsg .. "|" .. sql)
     return resultCode
end

--function ResetDailyWeekly(instanceID) -- no longer does daily tracking, so name is wrong.
    --checks for daily and weekly reset times.
    --if oldest date in daily/weekly table is over 22/(24 * 6.9) hours old, delete everything in the table.
    --local timeDiffDaily = os.time() - (60 * 60 * 22) --22 hours, converted to seconds.
    --local cmd = "DELETE FROM dailyVisited WHERE VisitedOn < " .. timeDiffDaily
    --Exec(cmd)
    --local timeDiffWeekly = os.time() - math.floor(60 * 60 * 24 * 6.9) -- 6.9 days, converted to seconds
    --cmd = "DELETE FROM weeklyVisited WHERE VisitedOn < " .. timeDiffWeekly
    --Exec(cmd)
    --cmd = "UPDATE weeklyPoints SET score = 0, instanceID = " .. instanceID
    --I need to get resetAt from the server.
    --Exec(cmd)

--end

function VisitedCell(pluscode)
    if (debugDB) then print("Checking if visited current cell " .. pluscode) end
    local query = "SELECT COUNT(*) as c FROM plusCodesVisited WHERE pluscode = '" .. pluscode .. "'"
    for i,row in ipairs(Query(query)) do
        if (row[1] == 1) then
            return true
        else
            return false
        end
    end
end

function VisitedCell8(pluscode)
    if (debugDB) then print("Checking if visited current cell8 " .. pluscode) end
    local query = "SELECT COUNT(*) as c FROM plusCodesVisited WHERE eightCode = '" .. pluscode .. "'"
    for i,row in ipairs(Query(query)) do
        if (row[1] >= 1) then --any number of entries over 1 means this block was visited.
            return true
        else
            return false
        end
    end
end

function TotalExploredCells()
    if (debugDB) then print("opening total explored cells ") end
    local query = "SELECT COUNT(*) as c FROM plusCodesVisited"
    for i,row in ipairs(Query(query)) do
        return row[1]
    end
end

function TotalExploredCell8s()
    if (debugDB) then print("opening total explored cell8s ") end
    local query = "SELECT COUNT(distinct eightCode) as c FROM plusCodesVisited"
    for i,row in ipairs(Query(query)) do
        return row[1]
    end
end

function Score()
    local query = "SELECT totalPoints as p from playerData"
    local qResults = Query(query)
    if (#qResults > 0) then
        for i,row in ipairs(qResults) do
            return row[1]
        end
    else
        return "?"
    end
end

function GetCommonLetters()
    openData()
    local query = "SELECT commonCodeLetters from Bounds"
    local results = SingleValueQuery(query)
    closeDb()
    return results
end

function LoadTerrainData(pluscode) --plus code does not contain a + here
    if (debugDB) then print("loading terrain data ") end
    openData()
    print(plusCode)
    if (plusCode == nil) then
        return {}
    end 

    --local query = "SELECT * FROM TerrainDataSmallTerrainInfo jointable INNER JOIN TerrainInfo ti on ti.id = jointable.TerrainInfoid INNER JOIN TerrainDataSmall td on td.id = jointable.TerrainDataSmallid WHERE ti.PlusCode = '" .. plusCode .. "'"
    local query = "SELECT * FROM "
    --Now looks like TIid|PlusCode|TDid|Name|AreatypeName --removed |OsmElementID|OsmElementType
    print(1)
    print(query)
    local results = Query(query)
    print(2)
    --print(query)
    --print(dump(results))
    --closeDb()
    return results
    --I think I want this to return results now, not the first row.
    -- for i,row in ipairs(results) do
    --     if (debugDB) then print(dump(row)) end
    --     return row
    -- end 
    -- return {} --empty table means no data found.
end

--This shouldn't be used anymore since I keep all that data in memory now.
function DownloadedCell8(pluscode)
    local query = "SELECT COUNT(*) as c FROM dataDownloaded WHERE pluscode8 = '" .. pluscode .. "'"
    for i,row in ipairs(Query(query)) do
        if (row[1] >= 1) then --any number of entries over 1 means this block was visited.
            return true
        else
            return false
        end
    end
    return false
end

function ClaimAreaLocally(mapdataid, name, score)
    if (debug) then print("claiming area " .. mapdataid) end
    name = string.gsub(name, "'", "''")
    local cmd = "INSERT INTO areasOwned (mapDataId, name, points) VALUES (" .. mapdataid .. ", '" .. name .. "'," .. score ..")"
    --db:exec(cmd)
    Exec(cmd)
end

function CheckAreaOwned(mapdataid)
    if (mapdataid == null) then return false end
    local query = "SELECT COUNT(*) as c FROM areasOwned WHERE MapDataId = "  .. mapdataid
    for i,row in ipairs(Query(query)) do
        if (row[1] >= 1) then --any number of entries over 1 means this entry is owned
            return true
        else
            return false
        end
    end
    return false
end

function AreaControlScore()
    local query = "SELECT SUM(points) FROM areasOwned"
    for i,row in ipairs(Query(query)) do
        if (#row == 1) then
            return row[1]
        else
            return 0
        end
    end
    return 0
end

function SpendPoints(points)
    local cmd = "UPDATE playerStats SET score = score - " .. points
    db:exec(cmd)
end

function AddPoints(points)
    local cmd = "UPDATE weeklyPoints SET score  = score + " .. points
    --print(cmd)
    local updated = db:exec(cmd)
    --print(updated)
    cmd = "update allTimePoints SET score = score + " .. points
    db:exec(cmd)
end

function AllTimePoints()
    local query = "SELECT score FROM playerStats"
    return SingleValueQuery(query)
end

function WeeklyPoints()
    local query = "SELECT score FROM weeklyPoints" --WHERE what?
    return SingleValueQuery(query)
end

function GetTeamID()
    local query = "SELECT factionID FROM playerData"
    for i,row in ipairs(Query(query)) do
        if (#row == 1) then
            return row[1]
        else
            return 0
        end
    end
    return 0
end

function GetServerAddress()
    local query = "SELECT serverAddress FROM systemData"
    for i,row in ipairs(Query(query)) do
        if (#row == 1) then
            return row[1]
        else
            return "noServerFound"
        end
    end
    return ""
end

-- function SetServerAddress(url)
--     local cmd = "UPDATE systemData SET serverAddress = '" .. url .. "'"
--     db:exec(cmd)
-- end

-- function SetFactionId(teamId)
--     local cmd = "UPDATE playerData SET factionID = " .. teamId .. ""
--     db:exec(cmd)
-- end

function GetEndDate(instanceID)
    local query = "SELECT endsAt FROM endDates WHERE instanceID = " ..instanceID
    return SingleValueQuery(query)
end

function SetEndDate(instanceID, endDate)
    local cmd = "UPDATE endDates SET endsAt = '" .. endDate .. "' WHERE instanceID = " .. instanceID
    --print(cmd)
    local updated = db:exec(cmd)
end

function GetPlacesInCell6(pluscode6)
    local query = "SELECT pi.placeInfoid, pi2.name, pi2.latCenter, pi2.lonCenter, pi2.width, pi2.height FROM PlaceIndexs pi INNER JOIN PlaceInfo2s pi2 on pi2.id == pi.placeInfoid WHERE pi.PlusCode = '" .. pluscode6 .. "' ORDER BY radius DESC"  --placeInfoId
    --print(query)
    --Schema:
    --      1           2        3           4          5       6    
    -- placeInfoID |  Name | LatCenter | lonCenter |  width | Height
    local data = Query(query)
    local results = {}
    for i, v in ipairs(data) do
        results[i] = v
    end

    print(dump(results))
    return results
end

function GetTrail(pluscode)
    --pluscode is 10 digits, no plus.
    local query = [[SELECT tds.name FROM TerrainInfo ti 
    INNER JOIN TerrainDataSmallTerrainInfo jointable on ti.id = jointable.TerrainInfoid 
    INNER JOIN TerrainDataSmall tds on tds.id = jointable.TerrainDataSmallid 
    WHERE ti.PlusCode = ']] .. pluscode .. "'"

    local results = Query(query)
    return results
end