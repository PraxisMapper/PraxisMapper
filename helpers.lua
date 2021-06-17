local sockets = require("socket")

--debugging helper function
function dump(o)
    if type(o) == 'table' then
       local s = '{ '
       for k,v in pairs(o) do
          if type(k) ~= 'number' then k = '"'..k..'"' end
          s = s .. '['..k..'] = ' .. dump(v) .. ','
       end
       return s .. '} '
    else
       return tostring(o)
    end
 end

 --Split a string, since there's no built in split in lua.
 function Split(s, delimiter)
   result = {};
   for match in (s..delimiter):gmatch("(.-)"..delimiter) do
       table.insert(result, match);
   end
   return result;
end

function doesFileExist( fname, path )
    local results = false
   -- Path for the file
   local filePath = system.pathForFile( fname, path )
   if ( filePath ) then
       local file, errorString = io.open( filePath, "r" )
       if not file then
           -- doesnt exist or an error locked it out
           print("file doesnt exist")
           print(errorString)
       else
            print("Exists")
           -- File exists!
           results = true
           -- Close the file handle
           file:close()
       end
   end
   return results
end

--not a real sleep function but close enough?
function sleep(sec)
   sockets.select(nil, nil, sec)
end

function copyFile(srcName, srcPath, dstName, dstPath, overwrite)

   local results = false

   local fileExists = doesFileExist(srcName, srcPath)
   if (fileExists == false) then
       return nil -- nil = Source file not found
   end

   -- Check to see if destination file already exists
   if not (overwrite) then
       if (doesFileExist(dstName, dstPath)) then
           return 1 -- 1 = File already exists (don't overwrite)
       end
   end

   -- Copy the source file to the destination file
   local rFilePath = system.pathForFile(srcName, srcPath)
   local wFilePath = system.pathForFile(dstName, dstPath)

   local rfh = io.open(rFilePath, "rb")
   local wfh, errorString = io.open(wFilePath, "wb")

   if not (wfh) then
       -- Error occurred; output the cause
       print("File error: " .. errorString)
       return false
   else
       -- Read the file and write to the destination directory
       local data = rfh:read("*a")
       if not (data) then
           print("Read error!")
           return false
       else
           if not (wfh:write(data)) then
               print("Write error!")
               return false
           end
       end
   end

   results = 2 -- 2 = File copied successfully!

   -- Close file handles
   rfh:close()
   wfh:close()

   return results
end

function CalcPresentRect(myLat, myLon, placeInfo)
    --If westLon < myLon < eastLon
    --and southLat < myLat < northlat
    --we are safely within the bounding box.
    if(debug) then print("Calcing rectangle present") end
    local widthMod = placeInfo[5] / 2
    local westBound = placeInfo[4] - widthMod  
    if westBound > myLon then  return false end
    
    local eastBound = placeInfo[4] + widthMod
    if eastBound < myLon then return false end

    local heightMod = placeInfo[6] / 2
    local southBound = placeInfo[3] - heightMod
    if southBound > myLat then  return false end
    
    local northBound = placeInfo[3] + heightMod
    if northBound < myLat then  return false end

    if (debug) then print("present: " .. placeInfo[2]) end
    return true
end

-- function CalcPresentCircle(myLat, myLon, placeInfo)
--     --EXTREMELY simple distance calculation. Not remotely concerned with errors on this estimate at this point
--     --print("a")
--     local distanceLat = math.abs(myLat - placeInfo[3])
--     local distanceLon = math.abs(myLon - placeInfo[4])
--     local distancePythag = distanceLat * distanceLat + distanceLon * distanceLon
--     local distanceDegrees = math.sqrt(distancePythag)
--     --print("B")
--     --print("Distance from " .. placeInfo[2] .. " is " .. distanceDegrees)
--     --print("radius for this place is " .. placeInfo[5])
--     --print("distance in miles is " .. DistanceInMiles(distanceLat, distanceLon))

--     if (placeInfo[5] == 0) then
--         placeInfo[5] = .000125 --treat as a Cell10, but also allows claiming from being adjacent to it
--     end
--     --print("c")

--     if distancePythag <= placeInfo[5] then
--         --print("d")
--         return true
--     end

--     return false
-- end

function DistanceInMiles(degreesAwayLat, degreesAwayLon)

    local milesAwayLat = degreesAwayLat * 69 --easy, lazy.
    local milesAwayLon = degreesAwayLon * math.cos(math.rad(degreesAwayLat)) * 69

    --and then pythag these values up for a straight-line estimate.
    local distancePythag = milesAwayLat * milesAwayLat + milesAwayLon * milesAwayLon
    local distanceMiles = math.sqrt(distancePythag)
    
    return distanceMiles
end