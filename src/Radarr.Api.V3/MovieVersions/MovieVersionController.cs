using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using Radarr.Http;
using Radarr.Http.REST;
using Radarr.Http.REST.Attributes;

namespace Radarr.Api.V3.MovieVersions
{
    [V3ApiController]
    public class MovieVersionController : RestController<MovieVersionResource>
    {
        private readonly IMovieVersionService _movieVersionService;
        private readonly IMovieService _movieService;
        private readonly IManageCommandQueue _commandQueueManager;

        public MovieVersionController(IMovieVersionService movieVersionService,
                                      IMovieService movieService,
                                      IManageCommandQueue commandQueueManager)
        {
            _movieVersionService = movieVersionService;
            _movieService = movieService;
            _commandQueueManager = commandQueueManager;
        }

        protected override MovieVersionResource GetResourceById(int id)
        {
            var version = _movieVersionService.Get(id);

            return version.ToResource(_movieVersionService.GetPrimary(version.MovieId).Id);
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<List<MovieVersionResource>> GetMovieVersions(int movieId)
        {
            return TypedResults.Ok(_movieVersionService.GetByMovieId(movieId).ToResource());
        }

        [RestPostById]
        [Consumes("application/json")]
        public Results<Created<MovieVersionResource>, NotFound> AddMovieVersion([FromBody] MovieVersionResource resource)
        {
            var movie = _movieService.GetMovie(resource.MovieId);

            // A pinned version is added by hand, for a file the user then picks for it
            if (resource.Pinned)
            {
                return Created(_movieVersionService.AddPinnedVersion(movie, resource.Name).Id);
            }

            var version = _movieVersionService.AddVersion(movie, resource.QualityProfileId, resource.Name, resource.RequiredCustomFormatIds);

            // Pick up files already present in the movie's folder that this version wants
            _commandQueueManager.Push(new RescanMovieCommand(movie.Id));

            return Created(version.Id);
        }

        [RestPutById]
        [Consumes("application/json")]
        public Results<Accepted<MovieVersionResource>, NotFound> UpdateMovieVersion([FromBody] MovieVersionResource resource)
        {
            var version = _movieVersionService.UpdateVersion(resource.Id, resource.Name, resource.QualityProfileId, resource.RequiredCustomFormatIds);

            // Coordinate the two services here so version resolution never depends on MovieService.
            if (_movieVersionService.GetPrimary(version.MovieId).Id == version.Id)
            {
                var movie = _movieService.GetMovie(version.MovieId);

                if (movie.QualityProfileId != version.QualityProfileId)
                {
                    movie.QualityProfileId = version.QualityProfileId;
                    _movieService.UpdateMovie(movie);
                }
            }

            return Accepted(version.Id);
        }

        [RestDeleteById]
        public Ok DeleteMovieVersion(int id)
        {
            _movieVersionService.RemoveVersion(id);

            return TypedResults.Ok();
        }
    }
}
