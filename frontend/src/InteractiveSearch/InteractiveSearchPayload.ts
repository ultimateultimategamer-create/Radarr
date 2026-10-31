interface MovieSearchPayload {
  movieId: number;

  // Limits the search to a single version of the movie (its primary version included)
  movieVersionId?: number;
}

type InteractiveSearchPayload = MovieSearchPayload;

export default InteractiveSearchPayload;
