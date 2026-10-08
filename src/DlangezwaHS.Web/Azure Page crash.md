The Extracurricular/Activities page crashes in Azure production with a 500 error. 
The error comes from ExtracurricularService.cs in the GetActivitiesAsync method.
Please read that file and the Activities Index view, find any null reference 
issues or missing null checks, and fix them. Also check if any properties 
like Period, Category, Venue, StartTime, EndTime need null-safe access.